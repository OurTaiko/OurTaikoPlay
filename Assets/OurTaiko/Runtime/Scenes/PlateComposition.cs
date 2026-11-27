using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OurTaiko.Chara;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // A complete nameplate is an ordered list of ordinary otanim views and live text: a layout of
    // the catalog of the set in use (NameplateItems.Set), or before there is one the text alone.
    // All artwork, including local masks and colour offsets, is baked into portable v1 clips.
    public sealed class PlateComposition
    {
        public sealed class Catalog
        {
            public readonly JObject States;
            // Each title is the items it wears (TitleParts); the plate draws no title any other way.
            public readonly IReadOnlyDictionary<string, TitleParts> Titles, AiTitles;
            public readonly string[] Blank;
            readonly HashSet<string>[] items = { new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) };

            internal Catalog(JObject data)
            {
                States = data["states"] as JObject ?? throw new InvalidDataException("Missing states");
                Titles = Parts(data["titles"] as JObject ?? throw new InvalidDataException("Missing titles"));
                AiTitles = Parts(data["aiTitles"] as JObject ?? throw new InvalidDataException("Missing AI titles"));
                Blank = data["blank"]?.ToObject<string[]>() ?? throw new InvalidDataException("Missing blank labels");
                // The look a plate falls back on when it names none there is.
                if (!Titles.ContainsKey("wood") || !AiTitles.ContainsKey("wood")) throw new InvalidDataException("Missing the wood title");
            }

            IReadOnlyDictionary<string, TitleParts> Parts(JObject options)
            {
                var parts = new Dictionary<string, TitleParts>(StringComparer.Ordinal);
                foreach (var option in options.Properties())
                {
                    var layers = option.Value as JObject ?? throw new InvalidDataException("Missing layers of " + option.Name);
                    string effect = (string)layers["effect"];
                    var title = new TitleParts((string)layers["band"] ?? throw new InvalidDataException("Missing band of " + option.Name), effect, (string)layers["icon"],
                        effect != null && ((bool?)layers["effectOverflow"] ?? false));
                    parts.Add(option.Name, title);
                    foreach (var slot in NameplateSlots.All)
                        if (title.In(slot) != null) items[(int)slot].Add(title.In(slot));
                }
                return parts;
            }

            // The set's items of a slot: those the titles are made of.
            public IEnumerable<string> Ids(NameplateSlot slot) => items[(int)slot];
            public bool Contains(NameplateSlot slot, string id) => id != null && items[(int)slot].Contains(id);

            public TitleParts Title(string choice) => Pick(Titles, choice);
            public TitleParts AiTitle(string choice) => Pick(AiTitles, choice);
            static TitleParts Pick(IReadOnlyDictionary<string, TitleParts> options, string choice) =>
                options.TryGetValue(choice ?? "", out var parts) ? parts : options["wood"];
        }

        // The object a plate's whole composition is drawn under, and the rank-up effect, which the
        // game carries (NameplateItems): it is played on a 段位 promotion, whatever set is in use.
        public const string RootName = "Complete nameplate", RankUpItem = "icon/lumen-rankup";

        // The catalog of the set in use; null when there is none yet.
        public static Catalog ReadCatalog() => NameplateItems.Catalog;

        readonly RectTransform root;
        readonly List<OtanimView> pictures = new List<OtanimView>();
        readonly List<TMP_Text> texts = new List<TMP_Text>();
        int pictureCount, textCount;
        OtanimView rankup;
        double rankStarted;
        long lastFrame = -1;
        public Canvas TextCanvas => textCount > 0 ? texts[0].canvas : null;
        public NameplateLayers Layers { get; }
        public TMP_Text PlayerName { get; private set; }
        public TMP_Text Title { get; private set; }

        public PlateComposition(RectTransform plate)
        {
            root = SkinUi.Rect(RootName, plate, NameplateLayout.Width, NameplateLayout.Height);
            root.pivot = new Vector2(0, 1);
            root.anchoredPosition = Vector2.zero;
            root.gameObject.layer = plate.gameObject.layer;
            root.gameObject.hideFlags = HideFlags.DontSave;
            Layers = new NameplateLayers(root);
        }

        public static string StateFor(PlayerInfo info, int side)
        {
            if (!string.IsNullOrEmpty(info.plateState) && info.plateState != "auto" && info.plateState != "custom"
                && ReadCatalog()?.States[info.plateState] != null) return info.plateState;
            string prefix = side == 1 ? "2p" : "1p";
            if (info.IsCoin) return prefix + "_coin";
            return prefix + (info.HasTitle || info.plateState == "custom" ? "_shougou" : "") + (info.HasDan ? "_dani" : "");
        }

        // The rows of a plate drawn before any set is here (a first launch that could not download
        // it): the title and the name where the layouts of its kind put them, nothing else. With no
        // band under it the title is white and outlined, as the name is.
        public static JArray TextOnly(PlayerInfo info)
        {
            var rows = new JArray();
            JObject Row(string role, float x, float y, float width, float size, float stroke, float shade) => new JObject
            {
                ["role"] = role, ["x"] = x, ["y"] = y, ["width"] = width, ["size"] = size, ["stroke"] = stroke,
                ["rgba"] = new JArray(shade, shade, shade, 1f),
            };
            if (info.HasTitle && !info.IsCoin)
                rows.Add(Row("text_title", NameplateLayout.TitleX, NameplateLayout.TitleY + NameplateLayout.TextLift(NameplateLayout.TitleFontSize),
                    NameplateLayout.TitleBoxWidth, NameplateLayout.TitleFontSize, NameplateLayout.NameOutline, 1));
            var name = NameplateLayout.NameBox(info);
            rows.Add(Row("text_name", name.X, name.Y + NameplateLayout.TextLift(name.FontSize), NameplateLayout.NameBoxWidth, name.FontSize, NameplateLayout.NameOutline, 1));
            return rows;
        }

        public static string RankLabel(int rank)
        {
            if (rank < PlayerInfo.DanFirst || rank >= PlayerInfo.DanCount) return null;
            int level = rank - PlayerInfo.DanFirst;
            return level < 5 ? (5-level) + "kyuu" : (level-4) + "dan";
        }

        internal static OtanimAsset RankAsset(PlayerInfo info, out string clip)
        {
            string ring = info.DanComboTier switch { 2 => "donfull", 1 => "full", _ => "clear" };
            string rank = RankLabel(info.dan) ?? "5kyuu";
            clip = rank + (info.gold ? "_gold" : "_red");
            return NameplateItems.CompositionPart("dan-" + ring);
        }

        // The title a plate of these rows shows, as the catalog names it (an unknown choice is wood),
        // and whether it is an AI title; null when the plate shows no title.
        public static string TitleOf(Catalog source, JArray rows, PlayerInfo info, out bool ai)
        {
            ai = false;
            foreach (var row in rows)
            {
                string role = (string)row["role"];
                if (role == "title_instance") return Known(source.Titles, info.plateTitle == "auto" ? info.profileBand : info.plateTitle);
                if (role == "ai_title_instance") { ai = true; return Known(source.AiTitles, info.plateAiTitle); }
            }
            return null;
        }

        static string Known(IReadOnlyDictionary<string, TitleParts> options, string choice) =>
            choice != null && options.ContainsKey(choice) ? choice : "wood";

        // The layers a plate wears: a custom plate wears the player's own pick of each slot, any other the
        // items its title is made of: the site's title's own layers when the plate follows the site
        // (auto) and the site gave them, else the catalog title. A custom plate whose band slot is
        // empty keeps its title's band.
        public static TitleParts Worn(Catalog source, JArray rows, PlayerInfo info)
        {
            string label = TitleOf(source, rows, info, out bool ai);
            var title = label == null ? default
                : ai ? source.AiTitle(label)
                : info.plateTitle == "auto" && info.ProfileLayers is TitleParts own ? own
                : source.Title(label);
            if (info.plateState != "custom" || info.IsCoin) return title;
            return new TitleParts(string.IsNullOrEmpty(info.bandItem) ? title.Band : info.bandItem, info.effectItem, info.iconItem,
                !string.IsNullOrEmpty(info.effectItem) && info.effectOverflow);
        }

        // Draws a layout's rows (source null: TextOnly's, with no set to draw pictures from).
        public void Apply(Catalog source, JArray rows, PlayerInfo info, int side)
        {
            string running = rankup?.clip;
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            pictureCount = textCount = 0;
            rankup = null;
            Title = PlayerName = null;
            Layers.Apply(source, source == null ? default : Worn(source, rows, info));
            bool badge = rows.Any(row => (string)row["role"] == "plate_player_no");
            var titleRow = rows.FirstOrDefault(row => (string)row["role"] == "text_title");
            foreach (var row in rows)
            {
                switch ((string)row["role"])
                {
                    case "base": Picture(NameplateItems.CompositionPart("plates"), (string)row["clip"]); break;
                    case "title_instance":
                    case "ai_title_instance":
                        // The title's text is printed on the band, under its effect.
                        Layers.PlaceBand();
                        if (titleRow != null) Title = Text(titleRow, info.HasTitle ? info.title : "");
                        Layers.PlaceEffect();
                        // A plate with no badge over its outline has nothing to put the icon behind.
                        if (!badge) Layers.PlaceIcon();
                        break;
                    case "plate_player_no":
                        Layers.PlaceIcon();
                        string state = StateFor(info, side);
                        bool second = state.StartsWith("2p", StringComparison.Ordinal) || !state.StartsWith("1p", StringComparison.Ordinal) && side == 1;
                        string badgeLabel = info.plateBadge is "1p" or "2p" ? info.plateBadge : second ? "2p" : "1p";
                        Picture(NameplateItems.CompositionPart("badges"), badgeLabel); break;
                    case "blank_instance": Picture(NameplateItems.CompositionPart("blank"), info.plateBlank ?? "off"); break;
                    case "dani":
                        var emblem = RankAsset(info, out string rankClip);
                        Picture(emblem, rankClip); break;
                    case "rankup_instance":
                        rankup = Picture(NameplateItems.Load(NameplateSlot.Icon, RankUpItem), running ?? "idle"); break;
                    case "text_title":
                        if (Title == null) Title = Text(row, info.HasTitle ? info.title : "");
                        break;
                    case "text_name":
                        PlayerName = Text(row, info.name ?? "");
                        break;
                }
            }
            for (int i = pictureCount; i < pictures.Count; i++) pictures[i].gameObject.SetActive(false);
            for (int i = textCount; i < texts.Count; i++) texts[i].gameObject.SetActive(false);
            Layers.FinishOrder();
            lastFrame = -1;
        }

        internal static OtanimView CreatePicture(string name, RectTransform parent)
        {
            var rect = SkinUi.Rect(name, parent, NameplateLayout.Width, NameplateLayout.Height);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.gameObject.layer = parent.gameObject.layer;
            rect.gameObject.hideFlags = HideFlags.DontSave;
            return rect.gameObject.AddComponent<OtanimView>();
        }

        OtanimView Picture(OtanimAsset asset, string clip)
        {
            if (asset?.File.Find(clip) == null) return null;
            if (pictureCount == pictures.Count)
            {
                pictures.Add(CreatePicture("Animation", root));
            }
            var view = pictures[pictureCount++];
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            view.Use(asset, clip);
            view.Show(0);
            return view;
        }

        TMP_Text Text(JToken row, string value)
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
            text.rectTransform.Center((float)row["x"], (float)row["y"] - NameplateLayout.TextLift(text.fontSize));
            text.text = value;
            UiFonts.Use(text, outlined: (float)row["stroke"] > 0);
            text.Squeeze((float)row["width"]);
            return text;
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
            Layers.Show(seconds);
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
