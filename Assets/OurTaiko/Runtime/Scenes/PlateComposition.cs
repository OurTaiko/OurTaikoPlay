using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OurTaiko.Chara;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // A complete nameplate is an ordered list of ordinary otanim views and live text.
    // All artwork, including local masks and colour offsets, is baked into portable v1 clips.
    public sealed class PlateComposition
    {
        public sealed class Catalog
        {
            public readonly JObject States, Titles, AiTitles;
            public readonly string[] Blank;
            readonly HashSet<string> bandItems = new HashSet<string>(StringComparer.Ordinal);
            public bool ContainsBand(string id) => bandItems.Contains(id);
            public readonly string BandBackingClip;
            internal Catalog(JObject data)
            {
                States = data["states"] as JObject ?? throw new InvalidDataException("Missing states");
                Titles = data["titles"] as JObject ?? throw new InvalidDataException("Missing titles");
                AiTitles = data["aiTitles"] as JObject ?? throw new InvalidDataException("Missing AI titles");
                foreach (var option in Titles.Properties()) bandItems.Add((string)option.Value);
                foreach (var option in AiTitles.Properties()) bandItems.Add((string)option.Value);
                // Older installed Lumen catalogs already contain this clip in plates.otanim.
                BandBackingClip = (string)data["bandBackingClip"] ?? "1p_shougou/4";
                Blank = data["blank"]?.ToObject<string[]>() ?? throw new InvalidDataException("Missing blank labels");
            }
        }

        static Catalog catalog;
        static string lastPath;
        static DateTime lastWrite;
        public static void ForgetCatalog() { catalog = null; lastPath = null; }

        public static Catalog ReadCatalog()
        {
            string path = Path.Combine(NameplateItems.Folder, "composition", "catalog.json");
            try
            {
                if (!File.Exists(path)) return null;
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (path == lastPath && stamp == lastWrite) return catalog;
                lastPath = path;
                lastWrite = stamp;
                catalog = null;
                catalog = new Catalog(JObject.Parse(File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidDataException)
            {
                Log.Warn("Nameplate", $"Could not read {path}: {e.Message}");
            }
            return catalog;
        }

        readonly RectTransform root;
        readonly List<OtanimView> pictures = new List<OtanimView>();
        readonly List<TMP_Text> texts = new List<TMP_Text>();
        int pictureCount, textCount;
        OtanimView rankup;
        double rankStarted;
        long lastFrame = -1;
        public Canvas TextCanvas => textCount > 0 ? texts[0].canvas : null;
        public bool Active => root.gameObject.activeSelf;

        public PlateComposition(RectTransform plate)
        {
            root = SkinUi.Rect("Complete nameplate", plate, NameplateLayout.Width, NameplateLayout.Height);
            root.pivot = new Vector2(0, 1);
            root.anchoredPosition = Vector2.zero;
            root.gameObject.layer = plate.gameObject.layer;
            root.gameObject.hideFlags = HideFlags.DontSave;
        }

        public static string StateFor(PlayerInfo info, int side)
        {
            if (info.plateState != "auto") return info.plateState;
            string prefix = side == 1 ? "2p" : "1p";
            if (info.IsCoin) return prefix + "_coin";
            return prefix + (info.HasTitle ? "_shougou" : "") + (info.HasDan ? "_dani" : "");
        }

        public static string RankLabel(int rank)
        {
            if (rank < PlayerInfo.DanFirst || rank >= PlayerInfo.DanCount) return null;
            int level = rank - PlayerInfo.DanFirst;
            return level < 5 ? (5-level) + "kyuu" : (level-4) + "dan";
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            rankup = null;
        }

        public void Apply(Catalog source, JArray rows, PlayerInfo info, int side)
        {
            string running = rankup?.clip;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            pictureCount = textCount = 0;
            rankup = null;
            foreach (var row in rows)
            {
                switch ((string)row["role"])
                {
                    case "base": Picture(NameplateItems.CompositionPart("plates"), (string)row["clip"]); break;
                    case "title_instance": Band(source.Titles, info.plateTitle); break;
                    case "ai_title_instance": Band(source.AiTitles, info.plateAiTitle); break;
                    case "plate_player_no":
                        string state = StateFor(info, side);
                        bool second = state.StartsWith("2p", StringComparison.Ordinal) || !state.StartsWith("1p", StringComparison.Ordinal) && side == 1;
                        string badge = info.plateBadge is "1p" or "2p" ? info.plateBadge : second ? "2p" : "1p";
                        Picture(NameplateItems.CompositionPart("badges"), badge); break;
                    case "blank_instance": Picture(NameplateItems.CompositionPart("blank"), info.plateBlank ?? "off"); break;
                    case "dani":
                        string ring = info.DanComboTier switch { 2 => "donfull", 1 => "full", _ => "clear" };
                        string rank = RankLabel(info.dan) ?? "5kyuu";
                        Picture(NameplateItems.CompositionPart("dan-" + ring), rank + (info.gold ? "_gold" : "_red")); break;
                    case "rankup_instance":
                        rankup = Picture(NameplateItems.Load(NameplateSlot.Icon, "icon/lumen-rankup"), running ?? "idle"); break;
                    case "text_title":
                        Text(row, info.HasTitle ? info.title : "", info.plateState == "auto"
                            ? new Vector2(NameplateLayout.TitleX, NameplateLayout.TitleY) : null);
                        break;
                    case "text_name":
                        var box = NameplateLayout.NameBox(info);
                        Text(row, info.name ?? "", info.plateState == "auto" ? new Vector2(box.X, box.Y) : null);
                        break;
                }
            }
            for (int i = pictureCount; i < pictures.Count; i++) pictures[i].gameObject.SetActive(false);
            for (int i = textCount; i < texts.Count; i++) texts[i].gameObject.SetActive(false);
            lastFrame = -1;
        }

        void Band(JObject options, string choice)
        {
            string id = (string)(options[choice ?? "wood"] ?? options["wood"]);
            Picture(NameplateItems.Load(NameplateSlot.Band, id), "idle");
        }

        OtanimView Picture(OtanimAsset asset, string clip)
        {
            if (asset?.File.Find(clip) == null) return null;
            if (pictureCount == pictures.Count)
            {
                var rect = SkinUi.Rect("Animation", root, NameplateLayout.Width, NameplateLayout.Height);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = Vector2.zero;
                rect.gameObject.layer = root.gameObject.layer;
                pictures.Add(rect.gameObject.AddComponent<OtanimView>());
            }
            var view = pictures[pictureCount++];
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            view.Use(asset, clip);
            view.Show(0);
            return view;
        }

        void Text(JToken row, string value, Vector2? center)
        {
            if (textCount == texts.Count)
            {
                var label = SkinUi.Text("Text", root, (float)row["size"]);
                label.gameObject.layer = root.gameObject.layer;
                texts.Add(label);
            }
            var text = texts[textCount++];
            text.gameObject.SetActive(true);
            text.transform.SetAsLastSibling();
            var rgba = row["rgba"];
            text.color = new Color((float)rgba[0],(float)rgba[1],(float)rgba[2],(float)rgba[3]);
            text.fontSize = (float)row["size"];
            text.characterSpacing = NameplateLayout.TextSpacing * 100 / text.fontSize;
            text.rectTransform.sizeDelta = new Vector2((float)row["width"], text.fontSize * 1.6f);
            var position = center ?? new Vector2((float)row["x"], (float)row["y"]);
            text.rectTransform.Center(position.x, position.y);
            text.text = value;
            UiFonts.Use(text, outlined: (float)row["stroke"] > 0);
            text.Squeeze((float)row["width"]);
        }

        public bool PlayRank(string clip, double at)
        {
            if (rankup == null || rankup.File.Find(clip) == null) return false;
            rankup.Use(rankup.asset, clip);
            rankStarted = at;
            rankup.Show(0);
            lastFrame = -1;
            return true;
        }

        public void RefreshText()
        {
            for (int i = 0; i < textCount; i++) texts[i].ForceMeshUpdate();
        }

        public void Show(double seconds)
        {
            long frame = (long)(seconds * 60);
            if (frame == lastFrame) return;
            lastFrame = frame;
            for (int i = 0; i < pictureCount; i++)
            {
                var view = pictures[i];
                if (view.Clip?.Frames.Length > 1)
                    view.Show(view == rankup ? Math.Max(0, seconds-rankStarted) : seconds);
            }
        }
    }
}
