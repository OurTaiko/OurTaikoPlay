using UnityEngine;

namespace OurTaiko
{
    // The faces of the result screen's "how was this chart?" prompt (Assets/OurTaiko/Art/rating,
    // drawn for this game by Documentation/rating-art.swift), in Resources. The prompt's plates,
    // panel and focus ring are マルチ演奏's (MultiArt).
    public sealed class RatingArt : ScriptableObject
    {
        public const string ResourceName = "RatingArt";

        [Tooltip("The don drum's face for each answer: liked it, not now, did not like it.")]
        public Sprite good;
        public Sprite skip, bad;

        static RatingArt loaded;
        public static RatingArt Load() => loaded != null ? loaded : loaded = Resources.Load<RatingArt>(ResourceName)
            ?? throw new System.InvalidOperationException("Missing Resources/" + ResourceName + ".");
    }
}
