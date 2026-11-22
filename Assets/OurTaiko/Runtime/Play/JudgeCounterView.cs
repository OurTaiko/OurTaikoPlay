using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Refined judgement counter (user design after the arcade's): a translucent orange panel beside
    // the lane's Don with 良 / 可 / 不可 / 連打数 bars (labels cut from result/score/max_combo_ja) and
    // each count right-aligned in the score counter's score_number digits, under an optional 良率 row:
    // (良 + 可) / (良 + 可 + 不可) to a tenth, the tenths and % smaller on the whole part's baseline.
    // Replaces the original's percentage JudgeCounter, which Nijiiro does not skin. Everything but
    // the digits is saved in the scene; the digits hang from each row's `counts` rect (and `rate`),
    // right edge on its right edge.
    public sealed class JudgeCounterView : MonoBehaviour
    {
        // score_number's cells: 56 × 64, glyphs from y 10 to the baseline at 53 (from the top), ink
        // from x 11 to 45. Neighbouring glyphs share Overlap px of outline, as in the score counter.
        public const float CellHeight = 64, Baseline = 53, Overlap = 4;
        public static readonly Vector2 DigitInk = new Vector2(11, 45);

        [Tooltip("score_number frames 0-9 (the score counter's digits).")]
        public Sprite[] digits;
        [Tooltip("Good, ok, bad, drumroll: the digits are right-aligned in each rect and fill its height.")]
        public RectTransform[] counts = new RectTransform[4];
        [Tooltip("Distance between digit starts, as a fraction of the digit height (score counter: 30 / 64).")]
        public float pitch = 30f / 64;

        [Header("良率 (optional)")]
        [Tooltip("The 良率 value, right-aligned in this rect; none on boards without the row.")]
        public RectTransform rate;
        [Tooltip("rate_dot and rate_percent (Tools/judge_counter_glyphs.swift): score_number's style, cropped to their ink.")]
        public Sprite rateDot, ratePercent;
        [Tooltip("Height of the whole part's cell; the tenths, point and % are rateSmall of it.")]
        public float rateHeight = 36;
        public float rateSmall = 0.78f;

        readonly List<Image>[] images = { new List<Image>(), new List<Image>(), new List<Image>(), new List<Image>() };
        readonly int[] shown = { -1, -1, -1, -1 };

        // 良率 glyphs left to right: three for the whole part, the point, the tenth, the %.
        Image[] rateGlyphs;
        int shownRate = -1;

        public string Text(int row) => shown[row] < 0 ? "" : shown[row].ToString();
        public Image Digit(int row, int index) => images[row][index];
        // The 良率 shown, as "97.3" (empty before the first Show, or without the row).
        public string RateText => shownRate < 0 ? "" : RateString(shownRate);
        public Image RateGlyph(int index) => rateGlyphs[index];

        public void Show(int good, int ok, int bad, int drumrolls)
        {
            ShowRow(0, good); ShowRow(1, ok); ShowRow(2, bad); ShowRow(3, drumrolls);
            if (rate != null) ShowRate(RateTenths(good, ok, bad));
        }

        // (良 + 可) / (良 + 可 + 不可) in tenths of a percent, cut rather than rounded so that one 不可
        // never reads 100.0; nothing judged yet is 100.0.
        public static int RateTenths(int good, int ok, int bad)
        {
            long hit = Mathf.Max(0, good) + (long)Mathf.Max(0, ok), total = hit + Mathf.Max(0, bad);
            return total == 0 ? 1000 : (int)(hit * 1000 / total);
        }

        public static string RateString(int tenths) => tenths / 10 + "." + tenths % 10;

        void ShowRate(int tenths)
        {
            if (shownRate == tenths) return;
            shownRate = tenths;
            if (rateGlyphs == null)
            {
                rateGlyphs = new Image[6];
                for (int i = 0; i < rateGlyphs.Length; i++) rateGlyphs[i] = SkinUi.Image("Glyph" + i, rate, null);
            }
            string whole = (tenths / 10).ToString();
            float full = rateHeight, small = rateHeight * rateSmall;
            // Right to left from the rect's right edge: %, tenth, point, then the whole part.
            float right = 0;
            right = PlaceGlyph(rateGlyphs[5], ratePercent, right, 0, ratePercent.rect.width, small, full);
            right = PlaceGlyph(rateGlyphs[4], digits[tenths % 10], right, DigitInk.x, DigitInk.y, small, full);
            right = PlaceGlyph(rateGlyphs[3], rateDot, right, 0, rateDot.rect.width, small, full);
            for (int slot = 2; slot >= 0; slot--)
            {
                int k = whole.Length - 3 + slot;
                rateGlyphs[slot].gameObject.SetActive(k >= 0);
                if (k >= 0) right = PlaceGlyph(rateGlyphs[slot], digits[whole[k] - '0'], right, DigitInk.x, DigitInk.y, full, full);
            }
        }

        // A glyph in a CellHeight-tall cell, `height` tall, with the ink between inkLeft and inkRight
        // (cell pixels) ending at `right` and its baseline on that of a `fullHeight` cell centred in
        // the rect. Returns where the next glyph's ink ends.
        static float PlaceGlyph(Image glyph, Sprite sprite, float right, float inkLeft, float inkRight, float height, float fullHeight)
        {
            glyph.sprite = sprite;
            float scale = height / CellHeight;
            var rect = glyph.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = sprite.rect.size * scale;
            float baseline = fullHeight * (0.5f - Baseline / CellHeight);
            rect.anchoredPosition = new Vector2(right + (sprite.rect.width - inkRight) * scale,
                baseline - height * (0.5f - Baseline / CellHeight));
            return right - (inkRight - inkLeft - Overlap) * scale;
        }

        void ShowRow(int row, int value)
        {
            value = Mathf.Max(0, value);
            if (shown[row] == value) return;
            shown[row] = value;
            var list = images[row];
            if (list.Count == 0) foreach (Transform child in counts[row]) list.Add(child.GetComponent<Image>());
            string text = value.ToString();
            while (list.Count < text.Length) list.Add(SkinUi.Image("Digit" + list.Count, counts[row], null));
            for (int i = 0; i < list.Count; i++)
            {
                bool used = i < text.Length;
                list[i].gameObject.SetActive(used);
                if (used) PlaceDigit(list[i], digits[text[i] - '0'], i, text.Length, counts[row].rect.height, pitch);
            }
        }

        // Digit i of n, right-aligned: its right edge is (n - 1 - i) pitches left of the rect's right edge.
        public static void PlaceDigit(Image digit, Sprite sprite, int index, int count, float height, float pitch)
        {
            digit.sprite = sprite;
            var rect = digit.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            float scale = height / sprite.rect.height;
            rect.sizeDelta = sprite.rect.size * scale;
            rect.anchoredPosition = new Vector2(-(count - 1 - index) * pitch * height, 0);
        }
    }
}
