using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // Which scene asks for a backdrop. Two players get only the headers and the drumroll effect;
    // 段位道場 and practice ask for the sets named DAN and PRACTICE whatever the chart says.
    public enum StageMode { Single, Double, Dan, Practice, Tutorial }

    // One set of a part of the play backdrop: an .otanim under Resources/Stage/<part>/<name>.
    [Serializable]
    public sealed class StageSet
    {
        public string name;
        [Tooltip("Drawn at random for charts that name no backdrop. Off for a set only a preset uses.")]
        public bool pooled = true;
        [Tooltip("Keep the clear layers in phase with the normal layers when a header changes state.")]
        public bool syncClear;
        [Tooltip("For drumroll effects, random horizontal and vertical offsets around each clip's baked origin. A single value applies to every clip.")]
        public Vector2[] hitSpread = Array.Empty<Vector2>();
    }

    // A troupe of dancers: Resources/Stage/dancers/<name>_0 … <name>_<members-1>, in the order they
    // come (centre, left, right, far left, far right), each one's whole timeline in its "rig" clip.
    [Serializable]
    public sealed class StageTroupe
    {
        public string name;
        [Tooltip("Drawn at random for charts that name no backdrop. Off for a troupe only a preset uses.")]
        public bool pooled = true;
        [Range(1, 5)] public int members = 5;
        [Tooltip("Rig frames (40 a beat) where the looped dance starts and where the way out starts.")]
        public int dance = 80, leave = 720;
        [Tooltip("Per-member exit frames for a troupe whose dances have different lengths.")]
        public int[] memberLeave = Array.Empty<int>();
        public int LeaveAt(int slot) => memberLeave != null && slot < memberLeave.Length ? memberLeave[slot] : leave;
        [Tooltip("The lead comes alone and the others join as the gauge fills, all five at the clear (a collab troupe). Off: three from the start.")]
        public bool leadFirst;
        [Tooltip("One file, <name>, whose rig clip is the whole troupe dancing, replayed on the beat; every member is always on stage.")]
        public bool movie;
    }

    // The backdrop a #SCENEPRESET asks for. A part left empty is drawn at random like any chart's;
    // "-" leaves that part out.
    [Serializable]
    public sealed class StagePack
    {
        public const string None = "-";

        public string preset;
        [Tooltip("Used only in solo play, like a script-backed collab in the source skin.")]
        public bool singleOnly;
        [Tooltip("Optional hit characters from Stage/chibi, with good_* and bad_* clips.")]
        public string chibi;
        public string header, scene, clear, troupe, footer, crowd, renda;
    }

    // What one play's backdrop is made of, by set name; null where it has none.
    public struct StageChoice
    {
        public string Preset; // the pack it came from, or null when drawn at random
        // Clear is "-" where the pack wants no clear scene at all.
        public string Header, Scene, Clear, Troupe, Footer, Crowd, Renda;
    }

    // Every backdrop set the game has, and how one is put together for a play (Nijiiro
    // background.lua): a chart whose #SCENEPRESET names a pack gets that pack, any other chart gets
    // each part drawn at random on its own.
    public sealed class StageCatalog : ScriptableObject
    {
        public const string Root = "Stage/";
        public const string HeaderFolder = "header", SceneFolder = "scene", ClearFolder = "clear", DancerFolder = "dancers",
            FooterFolder = "footer", CrowdFolder = "crowd", RendaFolder = "renda";
        public const string ChibiFolder = "chibi";
        public const string DanPreset = "DAN", PracticePreset = "PRACTICE", TutorialPreset = "TUTORIAL";

        [Tooltip("The band above the lane, 1920×276. Clips: normal (and normal_*, drawn in the file's order), clear. A file <name>_2p beside it is 2P's.")]
        public StageSet[] headers = Array.Empty<StageSet>();
        [Tooltip("The scene below the lane, 1920×540. Clips: normal (and normal_*).")]
        public StageSet[] scenes = Array.Empty<StageSet>();
        [Tooltip("The scene that takes the place of the one below the lane past the clear line, 1920×540. Clips: clear (and clear_*).")]
        public StageSet[] clears = Array.Empty<StageSet>();
        public StageTroupe[] troupes = Array.Empty<StageTroupe>();
        [Tooltip("The stand in front of the dancers, 1920×540. Clips: normal (and normal_*).")]
        public StageSet[] footers = Array.Empty<StageSet>();
        [Tooltip("The crowd of a full soul gauge, 1920×540, clip full.")]
        public StageSet[] crowds = Array.Empty<StageSet>();
        [Tooltip("What a drumroll hit throws over the header, 1920×276: every hit plays one of the file's clips once.")]
        public StageSet[] rendas = Array.Empty<StageSet>();
        public StagePack[] packs = Array.Empty<StagePack>();

        public static string Path(string folder, string name) => Root + folder + "/" + name;

        public StagePack Pack(string preset)
        {
            preset = preset?.Trim();
            if (string.IsNullOrEmpty(preset)) return null;
            foreach (var pack in packs)
                if (string.Equals(pack.preset?.Trim(), preset, StringComparison.OrdinalIgnoreCase)) return pack;
            return null;
        }

        public StageTroupe Troupe(string name) => Array.Find(troupes, t => t.name == name);

        // The pack a play in `mode` gets when asked for `presets`, the one that counts most first
        // (the chart's own, then its folders' from the nearest outwards): the first of them the
        // catalog has for that mode. A name it does not have asks for nothing, so the next one
        // decides; null when none does.
        public StagePack Asked(IReadOnlyList<string> presets, StageMode mode)
        {
            if (presets == null) return null;
            foreach (string preset in presets)
            {
                var pack = Pack(preset);
                if (pack != null && !(mode == StageMode.Double && pack.singleOnly)) return pack;
            }
            return null;
        }

        public StageChoice Choose(string preset, StageMode mode, System.Random random) => Choose(new[] { preset }, mode, random);

        // random is the caller's so a test can say what is drawn. has, when given, says which sets
        // have their files (part folder, set name): only those are drawn at random.
        public StageChoice Choose(IReadOnlyList<string> presets, StageMode mode, System.Random random, Func<string, string, bool> has = null)
        {
            var pack = mode switch
            {
                StageMode.Dan => Pack(DanPreset),
                StageMode.Practice => Pack(PracticePreset) ?? Asked(presets, mode),
                StageMode.Tutorial => Pack(TutorialPreset),
                _ => Asked(presets, mode),
            };
            var choice = new StageChoice
            {
                Preset = pack?.preset?.Trim(),
                Header = Pick(Names(headers), pack?.header, random, HeaderFolder, has),
                Renda = Pick(Names(rendas), pack?.renda, random, RendaFolder, has),
            };
            if (mode == StageMode.Double) return choice;
            choice.Scene = Pick(Names(scenes), pack?.scene, random, SceneFolder, has);
            // "-" is kept: it also takes away the clear a scene has of its own.
            choice.Clear = pack?.clear?.Trim() == StagePack.None ? StagePack.None : Pick(Names(clears), pack?.clear, random, ClearFolder, has);
            choice.Troupe = Pick(Names(troupes), pack?.troupe, random, DancerFolder, has);
            choice.Footer = Pick(Names(footers), pack?.footer, random, FooterFolder, has);
            choice.Crowd = Pick(Names(crowds), pack?.crowd, random, CrowdFolder, has);
            return choice;
        }

        // The files a choice is drawn from, as (part folder, file name), for a play with that many players.
        public IEnumerable<(string folder, string name)> Files(StageChoice choice, int players)
        {
            if (!string.IsNullOrEmpty(choice.Header))
            {
                yield return (HeaderFolder, choice.Header);
                if (players > 1) yield return (HeaderFolder, choice.Header + "_2p");
            }
            if (!string.IsNullOrEmpty(choice.Renda)) yield return (RendaFolder, choice.Renda);
            if (!string.IsNullOrEmpty(choice.Scene)) yield return (SceneFolder, choice.Scene);
            if (!string.IsNullOrEmpty(choice.Clear) && choice.Clear != StagePack.None) yield return (ClearFolder, choice.Clear);
            var troupe = Troupe(choice.Troupe);
            if (troupe != null)
            {
                if (troupe.movie) yield return (DancerFolder, troupe.name);
                else for (int i = 0; i < troupe.members; i++) yield return (DancerFolder, troupe.name + "_" + i);
            }
            if (!string.IsNullOrEmpty(choice.Footer)) yield return (FooterFolder, choice.Footer);
            if (!string.IsNullOrEmpty(choice.Crowd)) yield return (CrowdFolder, choice.Crowd);
            string chibi = Pack(choice.Preset)?.chibi?.Trim();
            if (!string.IsNullOrEmpty(chibi)) yield return (ChibiFolder, chibi);
        }

        // The file a set cannot be drawn without: a troupe's first dancer, else the set's own.
        public string MainFile(string folder, string set)
        {
            if (folder != DancerFolder) return set;
            var troupe = Troupe(set);
            return troupe != null && !troupe.movie ? set + "_0" : set;
        }

        // Every set this catalog lists, as (part folder, set name).
        public IEnumerable<(string folder, string set)> Sets()
        {
            foreach (var set in headers) yield return (HeaderFolder, set.name);
            foreach (var set in scenes) yield return (SceneFolder, set.name);
            foreach (var set in clears) yield return (ClearFolder, set.name);
            foreach (var troupe in troupes) yield return (DancerFolder, troupe.name);
            foreach (var set in footers) yield return (FooterFolder, set.name);
            foreach (var set in crowds) yield return (CrowdFolder, set.name);
            foreach (var set in rendas) yield return (RendaFolder, set.name);
            foreach (var pack in packs)
                if (!string.IsNullOrWhiteSpace(pack.chibi)) yield return (ChibiFolder, pack.chibi.Trim());
        }

        static List<(string name, bool pooled)> Names(StageSet[] sets)
        {
            var names = new List<(string, bool)>();
            foreach (var set in sets) names.Add((set.name, set.pooled));
            return names;
        }

        static List<(string name, bool pooled)> Names(StageTroupe[] sets)
        {
            var names = new List<(string, bool)>();
            foreach (var set in sets) names.Add((set.name, set.pooled));
            return names;
        }

        // asked is the pack's word for this part: a name, "-" for none, or nothing for any.
        static string Pick(List<(string name, bool pooled)> sets, string asked, System.Random random, string folder, Func<string, string, bool> has)
        {
            asked = asked?.Trim();
            if (asked == StagePack.None) return null;
            if (!string.IsNullOrEmpty(asked))
            {
                if (sets.Exists(s => s.name == asked)) return asked;
                Log.Warn("Stage", $"The stage catalog has no set named '{asked}'; one is drawn at random instead.");
            }
            var pool = sets.FindAll(s => s.pooled && (has == null || has(folder, s.name)));
            return pool.Count == 0 ? null : pool[random.Next(pool.Count)].name;
        }
    }
}
