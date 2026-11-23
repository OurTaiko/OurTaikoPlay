using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko
{
    // The stage catalog as the JSON a person reads and edits: the game's own (Resources/Stage/
    // catalog.json) and a player's (Stage/catalog.json in the game's folder), which adds to it.
    //
    //   {
    //     "version": 1,
    //     "presets": {
    //       "TOHO": { "header": "toho", "scene": "toho", "clear": "toho", "dancers": "toho", "footer": "-" }
    //     },
    //     "sets": {
    //       "header":  { "common_0": {}, "toho": { "random": false } },
    //       "dancers": { "toho": { "random": false, "leadFirst": true } }
    //     }
    //   }
    //
    // presets: what a chart's SCENEPRESET (or a box.def's #SCENEPRESET) names. Each part is a set's
    //   name, "-" for none, or left out to be drawn at random; "chibi" names the characters hits
    //   bring out, and "soloOnly": true keeps the preset to one-player plays.
    // sets: every set of each part, by the name of its file (Stage/<part>/<name>.otanim). A set is
    //   drawn at random for charts that name no backdrop unless "random" is false. A header may
    //   have "syncClear": true (its clear layers stay in phase with its normal ones); a renda,
    //   "spread": [[x, y], ...] (how far each clip is thrown from its place); dancers, "members"
    //   (1 to 5, files <name>_0 ...), "dance" and "leave" (rig frames, 40 a beat, where the looped
    //   dance and the way out start), "leaveEach" (a way out per dancer), "leadFirst": true (the
    //   lead comes alone and the rest join as the gauge fills) or "movie": true (one file, the
    //   whole troupe). Comments (// and /* */) are allowed.
    public static class StageCatalogFile
    {
        public const int Version = 1;

        // The part folders, in the order they are written.
        static readonly string[] Parts =
        {
            StageCatalog.HeaderFolder, StageCatalog.SceneFolder, StageCatalog.ClearFolder, StageCatalog.DancerFolder,
            StageCatalog.FooterFolder, StageCatalog.CrowdFolder, StageCatalog.RendaFolder,
        };
        const string Random = "random", SyncClear = "syncClear", Spread = "spread", Members = "members", Dance = "dance", Leave = "leave",
            LeaveEach = "leaveEach", LeadFirst = "leadFirst", Movie = "movie", SoloOnly = "soloOnly";

        static StageSet[] Sets(StageCatalog catalog, string part) => part switch
        {
            StageCatalog.HeaderFolder => catalog.headers,
            StageCatalog.SceneFolder => catalog.scenes,
            StageCatalog.ClearFolder => catalog.clears,
            StageCatalog.FooterFolder => catalog.footers,
            StageCatalog.CrowdFolder => catalog.crowds,
            _ => catalog.rendas,
        };

        static void SetSets(StageCatalog catalog, string part, StageSet[] sets)
        {
            switch (part)
            {
                case StageCatalog.HeaderFolder: catalog.headers = sets; break;
                case StageCatalog.SceneFolder: catalog.scenes = sets; break;
                case StageCatalog.ClearFolder: catalog.clears = sets; break;
                case StageCatalog.FooterFolder: catalog.footers = sets; break;
                case StageCatalog.CrowdFolder: catalog.crowds = sets; break;
                default: catalog.rendas = sets; break;
            }
        }

        static string PartOf(StagePack pack, string part) => part switch
        {
            StageCatalog.HeaderFolder => pack.header,
            StageCatalog.SceneFolder => pack.scene,
            StageCatalog.ClearFolder => pack.clear,
            StageCatalog.DancerFolder => pack.troupe,
            StageCatalog.FooterFolder => pack.footer,
            StageCatalog.CrowdFolder => pack.crowd,
            _ => pack.renda,
        };

        static void SetPart(StagePack pack, string part, string name)
        {
            switch (part)
            {
                case StageCatalog.HeaderFolder: pack.header = name; break;
                case StageCatalog.SceneFolder: pack.scene = name; break;
                case StageCatalog.ClearFolder: pack.clear = name; break;
                case StageCatalog.DancerFolder: pack.troupe = name; break;
                case StageCatalog.FooterFolder: pack.footer = name; break;
                case StageCatalog.CrowdFolder: pack.crowd = name; break;
                default: pack.renda = name; break;
            }
        }

        // ---------------------------------------------------------------- writing

        static string Quote(string text) => JToken.FromObject(text ?? "").ToString(Newtonsoft.Json.Formatting.None);
        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // One entry a line, so a preset or a set is read, copied and changed as a line.
        public static string Write(StageCatalog catalog)
        {
            var text = new StringBuilder();
            text.Append("{\n  \"version\": ").Append(Version).Append(",\n\n  \"presets\": {\n");
            var presets = new List<string>();
            foreach (var pack in catalog.packs)
            {
                var fields = new List<string>();
                if (pack.singleOnly) fields.Add($"\"{SoloOnly}\": true");
                foreach (string part in Parts)
                {
                    string name = PartOf(pack, part)?.Trim();
                    if (!string.IsNullOrEmpty(name)) fields.Add($"\"{part}\": {Quote(name)}");
                }
                if (!string.IsNullOrWhiteSpace(pack.chibi)) fields.Add($"\"{StageCatalog.ChibiFolder}\": {Quote(pack.chibi.Trim())}");
                presets.Add($"    {Quote(pack.preset.Trim())}: {{ {string.Join(", ", fields)} }}");
            }
            text.Append(string.Join(",\n", presets)).Append("\n  },\n\n  \"sets\": {\n");
            var parts = new List<string>();
            foreach (string part in Parts)
            {
                var lines = new List<string>();
                if (part == StageCatalog.DancerFolder)
                {
                    var defaults = new StageTroupe();
                    foreach (var troupe in catalog.troupes)
                    {
                        var fields = new List<string>();
                        if (!troupe.pooled) fields.Add($"\"{Random}\": false");
                        if (troupe.movie) fields.Add($"\"{Movie}\": true");
                        else
                        {
                            if (troupe.leadFirst) fields.Add($"\"{LeadFirst}\": true");
                            if (troupe.members != defaults.members) fields.Add($"\"{Members}\": {troupe.members}");
                            if (troupe.dance != defaults.dance) fields.Add($"\"{Dance}\": {troupe.dance}");
                            if (troupe.leave != defaults.leave) fields.Add($"\"{Leave}\": {troupe.leave}");
                            if (troupe.memberLeave != null && troupe.memberLeave.Length > 0) fields.Add($"\"{LeaveEach}\": [{string.Join(", ", troupe.memberLeave)}]");
                        }
                        lines.Add(Line(troupe.name, fields));
                    }
                }
                else
                    foreach (var set in Sets(catalog, part))
                    {
                        var fields = new List<string>();
                        if (!set.pooled) fields.Add($"\"{Random}\": false");
                        if (set.syncClear) fields.Add($"\"{SyncClear}\": true");
                        if (set.hitSpread != null && set.hitSpread.Length > 0)
                            fields.Add($"\"{Spread}\": [{string.Join(", ", set.hitSpread.Select(v => $"[{Number(v.x)}, {Number(v.y)}]"))}]");
                        lines.Add(Line(set.name, fields));
                    }
                parts.Add($"    \"{part}\": {{\n{string.Join(",\n", lines)}\n    }}");
            }
            text.Append(string.Join(",\n", parts)).Append("\n  }\n}\n");
            return text.ToString();
        }

        static string Line(string name, List<string> fields) => $"      {Quote(name)}: {(fields.Count == 0 ? "{}" : "{ " + string.Join(", ", fields) + " }")}";

        // ---------------------------------------------------------------- reading

        // A catalog read from its text. Throws FormatException, saying where, for a file that is
        // not one; warnings are told of what was passed over (a key this game does not know).
        public static StageCatalog Read(string json, Action<string> warn = null)
        {
            JObject root;
            try { root = JObject.Parse(json, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }); }
            catch (Newtonsoft.Json.JsonException error) { throw new FormatException("it is not JSON: " + error.Message); }
            if (root["version"]?.Type != JTokenType.Integer) throw new FormatException("\"version\" is missing; this game writes " + Version);
            int version = (int)root["version"];
            if (version < 1 || version > Version) throw new FormatException($"\"version\" is {version}; this game reads up to {Version}");
            foreach (var property in root.Properties())
                if (property.Name != "version" && property.Name != "presets" && property.Name != "sets") warn?.Invoke($"\"{property.Name}\" is not something a catalog has");

            var catalog = ScriptableObject.CreateInstance<StageCatalog>();
            catalog.hideFlags = HideFlags.DontSave;
            try
            {
                var sets = Object(root, "sets", "sets") ?? new JObject();
                foreach (var property in sets.Properties())
                    if (System.Array.IndexOf(Parts, property.Name) < 0) warn?.Invoke($"sets.{property.Name} is not a part (the parts are {string.Join(", ", Parts)})");
                foreach (string part in Parts)
                {
                    var listed = Object(sets, part, "sets." + part) ?? new JObject();
                    if (part == StageCatalog.DancerFolder) catalog.troupes = listed.Properties().Select(p => Troupe(p, "sets." + part, warn)).ToArray();
                    else SetSets(catalog, part, listed.Properties().Select(p => Set(p, part, "sets." + part, warn)).ToArray());
                }
                var presets = Object(root, "presets", "presets") ?? new JObject();
                catalog.packs = presets.Properties().Select(p => Pack(p, warn)).ToArray();
                return catalog;
            }
            catch
            {
                Destroy(catalog);
                throw;
            }
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        static JObject Object(JObject from, string key, string where)
        {
            var token = from[key];
            if (token == null || token.Type == JTokenType.Null) return null;
            return token as JObject ?? throw new FormatException($"{where} must be {{ ... }}");
        }

        static JObject Entry(JProperty property, string where)
        {
            if (property.Name.Trim().Length == 0) throw new FormatException($"{where} has an entry without a name");
            return property.Value as JObject ?? throw new FormatException($"{where}.{property.Name} must be {{ ... }} ({{}} when it has nothing to say)");
        }

        static bool Flag(JObject from, string key, bool otherwise, string where)
        {
            var token = from[key];
            if (token == null) return otherwise;
            return token.Type == JTokenType.Boolean ? (bool)token : throw new FormatException($"{where}.{key} must be true or false");
        }

        static int Whole(JObject from, string key, int otherwise, string where)
        {
            var token = from[key];
            if (token == null) return otherwise;
            return token.Type == JTokenType.Integer ? (int)token : throw new FormatException($"{where}.{key} must be a whole number");
        }

        static string Text(JObject from, string key, string where)
        {
            var token = from[key];
            if (token == null) return "";
            return token.Type == JTokenType.String ? ((string)token).Trim() : throw new FormatException($"{where}.{key} must be a name in quotes");
        }

        static void Unknown(JObject entry, string where, Action<string> warn, params string[] known)
        {
            foreach (var property in entry.Properties())
                if (System.Array.IndexOf(known, property.Name) < 0) warn?.Invoke($"{where}.{property.Name} is not known here (known: {string.Join(", ", known)})");
        }

        static StageSet Set(JProperty property, string part, string where, Action<string> warn)
        {
            var entry = Entry(property, where);
            where += "." + property.Name;
            var set = new StageSet { name = property.Name.Trim(), pooled = Flag(entry, Random, true, where) };
            if (part == StageCatalog.HeaderFolder)
            {
                Unknown(entry, where, warn, Random, SyncClear);
                set.syncClear = Flag(entry, SyncClear, false, where);
            }
            else if (part == StageCatalog.RendaFolder)
            {
                Unknown(entry, where, warn, Random, Spread);
                if (entry[Spread] != null)
                {
                    if (!(entry[Spread] is JArray pairs)) throw new FormatException($"{where}.{Spread} must be [[x, y], ...]");
                    set.hitSpread = pairs.Select(pair =>
                    {
                        if (!(pair is JArray xy) || xy.Count != 2 || xy.Any(n => n.Type != JTokenType.Integer && n.Type != JTokenType.Float))
                            throw new FormatException($"{where}.{Spread} must be [[x, y], ...], each of two numbers");
                        return new Vector2((float)xy[0], (float)xy[1]);
                    }).ToArray();
                }
            }
            else Unknown(entry, where, warn, Random);
            return set;
        }

        static StageTroupe Troupe(JProperty property, string where, Action<string> warn)
        {
            var entry = Entry(property, where);
            where += "." + property.Name;
            Unknown(entry, where, warn, Random, Members, Dance, Leave, LeaveEach, LeadFirst, Movie);
            var troupe = new StageTroupe { name = property.Name.Trim() };
            troupe.pooled = Flag(entry, Random, true, where);
            troupe.movie = Flag(entry, Movie, false, where);
            troupe.leadFirst = Flag(entry, LeadFirst, false, where);
            troupe.members = Whole(entry, Members, troupe.members, where);
            troupe.dance = Whole(entry, Dance, troupe.dance, where);
            troupe.leave = Whole(entry, Leave, troupe.leave, where);
            if (troupe.members < 1 || troupe.members > 5) throw new FormatException($"{where}.{Members} must be 1 to 5");
            if (troupe.dance < 0 || troupe.leave <= troupe.dance) throw new FormatException($"{where}: {Leave} must come after {Dance}, and neither before 0");
            if (entry[LeaveEach] != null)
            {
                if (!(entry[LeaveEach] is JArray frames) || frames.Any(f => f.Type != JTokenType.Integer)) throw new FormatException($"{where}.{LeaveEach} must be [frame, ...], whole numbers");
                troupe.memberLeave = frames.Select(f => (int)f).ToArray();
            }
            return troupe;
        }

        static StagePack Pack(JProperty property, Action<string> warn)
        {
            var entry = Entry(property, "presets");
            string where = "presets." + property.Name;
            Unknown(entry, where, warn, Parts.Append(StageCatalog.ChibiFolder).Append(SoloOnly).ToArray());
            var pack = new StagePack { preset = property.Name.Trim(), singleOnly = Flag(entry, SoloOnly, false, where), chibi = Text(entry, StageCatalog.ChibiFolder, where) };
            foreach (string part in Parts) SetPart(pack, part, Text(entry, part, where));
            return pack;
        }

        // ---------------------------------------------------------------- merging

        // Adds `over` to `catalog`: a preset or a set of a name it already has takes that one's
        // place, as it is written in `over`; the rest are added after the ones it had.
        public static void Merge(StageCatalog catalog, StageCatalog over)
        {
            foreach (string part in Parts)
            {
                if (part == StageCatalog.DancerFolder) catalog.troupes = Merged(catalog.troupes, over.troupes, t => t.name);
                else SetSets(catalog, part, Merged(Sets(catalog, part), Sets(over, part), s => s.name));
            }
            catalog.packs = Merged(catalog.packs, over.packs, p => p.preset.Trim().ToUpperInvariant());
        }

        static T[] Merged<T>(T[] had, T[] over, Func<T, string> name)
        {
            var all = new List<T>(had);
            foreach (var item in over)
            {
                int at = all.FindIndex(x => name(x) == name(item));
                if (at >= 0) all[at] = item; else all.Add(item);
            }
            return all.ToArray();
        }
    }
}
