using UnityEngine;

namespace OurTaiko.Multi
{
    // The art and sounds of マルチ演奏 (Assets/OurTaiko/Art/multi, drawn for this game by
    // Documentation/multi-art.swift), in Resources so the room screen, the play scene's scoreboard
    // and the banner over other screens all read the one asset.
    public sealed class MultiArt : ScriptableObject
    {
        public const string ResourceName = "MultiArt";

        [Header("Room")]
        public Sprite background;
        [Tooltip("Nine-sliced: a dark panel, a white plate to tint, its lighter inset, the focus ring, a rounded bar, a speech bubble, a ribbon.")]
        public Sprite panel;
        public Sprite plate, inset, focus, bar, bubble, ribbon;

        [Header("Icons")]
        [Tooltip("A white disc to tint: avatars and rank badges.")]
        public Sprite disc;
        public Sprite crownHost, crownClear, crownFullCombo, crownDonderful, check, wait, offline, locked, open, clock, mark;
        [Tooltip("The sixteen stamps, by id.")]
        public Sprite[] stamps;

        [Header("The skin's own")]
        [Tooltip("The five courses' icons, Easy to Ura (the lane's lane_difficulty frames).")]
        public Sprite[] courseIcons;
        [Tooltip("The result screen's labels (result/score/max_combo_ja): 良, 可, 不可, 連打数, 最大コンボ数.")]
        public Sprite judgeGood;
        public Sprite judgeOk, judgeBad, labelDrumroll, labelMaxCombo;

        [Header("Sounds")]
        public AudioClip don;
        public AudioClip ka, bgm;

        static MultiArt loaded;
        public static MultiArt Load() => loaded != null ? loaded : loaded = Resources.Load<MultiArt>(ResourceName)
            ?? throw new System.InvalidOperationException("Missing Resources/" + ResourceName + ".");

        // A member's colour by their place in the room.
        public static readonly Color[] SlotColors =
        {
            new Color(1f, 0.35f, 0.21f), new Color(0.27f, 0.78f, 0.91f), new Color(1f, 0.79f, 0.24f),
            new Color(0.38f, 0.84f, 0.48f), new Color(0.66f, 0.52f, 0.94f), new Color(1f, 0.5f, 0.69f),
            new Color(0.55f, 0.6f, 0.7f), new Color(0.95f, 0.6f, 0.3f),
        };
        public static Color SlotColor(int slot) => SlotColors[((slot % SlotColors.Length) + SlotColors.Length) % SlotColors.Length];

        // A course's icon; null for a course the skin has none for.
        public Sprite CourseIcon(string course)
        {
            int index = course switch { "Easy" => 0, "Normal" => 1, "Hard" => 2, "Oni" => 3, "Edit" => 4, _ => -1 };
            return courseIcons != null && index >= 0 && index < courseIcons.Length ? courseIcons[index] : null;
        }

        // The five courses' colours, as the song list shows them.
        public static Color CourseColor(string course) => course switch
        {
            "Easy" => new Color(0.93f, 0.26f, 0.2f), "Normal" => new Color(0.42f, 0.7f, 0.2f), "Hard" => new Color(0.2f, 0.53f, 0.75f),
            "Oni" => new Color(0.85f, 0.2f, 0.55f), "Edit" => new Color(0.48f, 0.3f, 0.82f), _ => new Color(0.4f, 0.4f, 0.45f),
        };
    }

    // The mode's text, from the Multi string table.
    public static class MultiText
    {
        public const string Table = "Multi";
        public static string Get(string key) => L10n.Get(Table, key);
        public static string Format(string key, params object[] args) => L10n.Format(Table, key, args);

        // What a server or connection code means to the player.
        public static string Describe(string code)
            => string.IsNullOrEmpty(code) ? "" : L10n.Has(Table, "error." + code) ? Get("error." + code) : Format("error.unknown", code);

        public static string Course(string course) => course switch
        {
            "Easy" => L10n.Get("Common", "difficulty.easy"), "Normal" => L10n.Get("Common", "difficulty.normal"),
            "Hard" => L10n.Get("Common", "difficulty.hard"), "Oni" => L10n.Get("Common", "difficulty.oni"),
            "Edit" => L10n.Get("Common", "difficulty.ura"), _ => course ?? "",
        };

        public static string Stamp(int id) => Get("stamp." + id);

        // A nickname as shown: the server's, or a placeholder for an empty one.
        public static string Name(string nickname) => string.IsNullOrWhiteSpace(nickname) ? Get("player.unnamed") : nickname;
    }
}
