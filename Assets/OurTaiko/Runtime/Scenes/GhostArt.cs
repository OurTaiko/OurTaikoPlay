using UnityEngine;

namespace OurTaiko
{
    // ゴーストバトル's own art (Assets/OurTaiko/Art/ghost, drawn for this game by
    // Documentation/ghost-art.swift), in Resources. The plates, panels and icons of its screen are
    // マルチ演奏's (MultiArt).
    public sealed class GhostArt : ScriptableObject
    {
        public const string ResourceName = "GhostArt";

        public Sprite background;
        [Tooltip("The mode's mark, the face of a recorded player, a wisp, and the VS emblem.")]
        public Sprite mark;
        public Sprite face, wisp, versus;

        static GhostArt loaded;
        public static GhostArt Load() => loaded != null ? loaded : loaded = Resources.Load<GhostArt>(ResourceName)
            ?? throw new System.InvalidOperationException("Missing Resources/" + ResourceName + ".");
    }
}
