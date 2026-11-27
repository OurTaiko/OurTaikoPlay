using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // The writing system a text is in, which picks its UI font (UiFontSet).
    public enum TextScript
    {
        // Nothing that belongs to one script: Latin, digits, symbols, or ideographs every script shares.
        Neutral,
        Japanese,
        SimplifiedChinese,
        TraditionalChinese,
        // Never detected: the English interface's script, so Neutral text there gets the Latin face.
        // Appended last because UiFontSet.asset stores scripts by their enum value.
        Latin,
    }

    // Tells the script of a text from its characters. Kana decide on their own; an ideograph
    // counts only if one script alone writes it (Resources/UiFonts/HanScripts.txt, written by
    // Tools/han_scripts.py), so 们 is Simplified, 們 Traditional, 桜 Japanese and 国 none of them.
    // The script with the most such characters wins; ties go Japanese, Traditional, Simplified. No UI
    // face has hangul, so Korean is not told apart.
    public static class TextScripts
    {
        public const string TablePath = "UiFonts/HanScripts";

        static HashSet<char> japanese, simplified, traditional;

        public static TextScript Classify(string text)
        {
            if (string.IsNullOrEmpty(text)) return TextScript.Neutral;
            if (japanese == null) LoadTable();
            int ja = 0, hant = 0, hans = 0;
            foreach (char c in text)
            {
                if (IsKana(c)) ja++;
                else if (c < 0x3400) continue;
                else if (japanese.Contains(c)) ja++;
                else if (traditional.Contains(c)) hant++;
                else if (simplified.Contains(c)) hans++;
            }
            int best = Mathf.Max(ja, Mathf.Max(hant, hans));
            return best == 0 ? TextScript.Neutral
                : ja == best ? TextScript.Japanese
                : hant == best ? TextScript.TraditionalChinese
                : TextScript.SimplifiedChinese;
        }

        // Hiragana, katakana (without the middle dot ・, which Chinese uses too), their extensions, the
        // iteration mark 々 and half-width katakana.
        static bool IsKana(char c) =>
            (c >= 0x3041 && c <= 0x30FF && c != 0x30FB) || (c >= 0x31F0 && c <= 0x31FF) || c == 0x3005 || (c >= 0xFF66 && c <= 0xFF9D);

        static void LoadTable()
        {
            var asset = Resources.Load<TextAsset>(TablePath)
                ?? throw new System.InvalidOperationException("Missing " + TablePath + " table.");
            string[] lines = asset.text.Split('\n');
            if (lines.Length < 3) throw new System.InvalidOperationException(TablePath + " needs three lines.");
            traditional = new HashSet<char>(lines[2]);
            simplified = new HashSet<char>(lines[1]);
            japanese = new HashSet<char>(lines[0]);
            Resources.UnloadAsset(asset);
        }
    }
}
