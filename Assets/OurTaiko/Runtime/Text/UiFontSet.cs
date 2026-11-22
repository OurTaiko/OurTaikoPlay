using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko
{
    // The UI fonts and the scripts each one is for (Resources/UiFonts/UiFontSet.asset, built by the
    // Editor's UiFontBuilder). A text is drawn in one face only: Pick takes the face of the text's
    // script if it has every character, otherwise the first face that has them all, so a missing
    // glyph changes the whole text's face rather than mixing two faces in one line. Font fallbacks only
    // catch what no face has.
    public sealed class UiFontSet : ScriptableObject
    {
        public const string ResourcePath = "UiFonts/UiFontSet";

        [Serializable]
        public sealed class Face
        {
            public TMP_FontAsset font;
            // The font's material with the shared black outline (light text); dark text uses font.material.
            public Material outline;
            public TextScript[] scripts = Array.Empty<TextScript>();

            [NonSerialized] Dictionary<char, bool> covered;

            // Whether the source font file has a glyph for c. Asking never adds glyphs to an atlas, and
            // unlike Font.HasCharacter it does not count the system fonts Unity falls back on. TMP loads
            // its own face before every glyph it adds, so moving FontEngine's current face here is safe.
            public bool Has(char c)
            {
                if (c < 0x20 || char.IsWhiteSpace(c)) return true;
                covered ??= new Dictionary<char, bool>();
                if (!covered.TryGetValue(c, out bool has))
                    covered[c] = has = font.sourceFontFile != null
                        && FontEngine.LoadFontFace(font.sourceFontFile) == FontEngineError.Success
                        && FontEngine.TryGetGlyphIndex(c, out uint glyph) && glyph != 0;
                return has;
            }

            public bool IsFor(TextScript script) => Array.IndexOf(scripts, script) >= 0;
        }

        // In order of preference when the text's own face lacks a character.
        [SerializeField] Face[] faces = Array.Empty<Face>();

        public IReadOnlyList<Face> Faces => faces;

        public Face FaceOf(TMP_FontAsset font)
        {
            if (font == null) return null;
            foreach (var face in faces)
                if (face.font == font) return face;
            return null;
        }

        // The face for script; Neutral and unlisted scripts get the first face.
        public Face FaceFor(TextScript script)
        {
            foreach (var face in faces)
                if (face.IsFor(script)) return face;
            return faces[0];
        }

        // The face to draw text in. script is what Neutral text resolves to (the interface language's).
        public Face Pick(string text, TextScript script)
        {
            var detected = TextScripts.Classify(text);
            var preferred = FaceFor(detected == TextScript.Neutral ? script : detected);
            int fewest = Missing(preferred, text, int.MaxValue);
            if (fewest == 0) return preferred;
            var best = preferred;
            foreach (var face in faces)
            {
                if (face == preferred) continue;
                int missing = Missing(face, text, fewest);
                if (missing >= fewest) continue;
                best = face;
                fewest = missing;
                if (missing == 0) break;
            }
            return best;
        }

        // How many characters of text the face lacks, counting no further than limit.
        static int Missing(Face face, string text, int limit)
        {
            int missing = 0;
            if (text == null) return 0;
            for (int i = 0; i < text.Length && missing < limit; i++)
            {
                char c = text[i];
                // Rich-text tags are markup, not characters to draw.
                if (c == '<')
                {
                    int end = text.IndexOf('>', i + 1);
                    if (end > i) { i = end; continue; }
                }
                if (!face.Has(c)) missing++;
            }
            return missing;
        }
    }
}
