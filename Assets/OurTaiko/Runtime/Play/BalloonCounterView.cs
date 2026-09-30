using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OurTaiko
{
    public sealed class BalloonCounterView : MonoBehaviour
    {
        public UnityEngine.UI.Image bubble, body;
        public RectTransform number;
        public CanvasGroup visuals;
        public Sprite[] digitSprites, bodyFrames;

        public const double PopFadeSeconds = 0.166;
        public int NoteIndex { get; private set; } = -1;
        public int Remaining { get; private set; }
        public bool IsVisible => NoteIndex >= 0;
        public bool IsPopped { get; private set; }
        readonly List<UnityEngine.UI.Image> digits = new List<UnityEngine.UI.Image>();
        static readonly int[] InflationFrames = { 0, 2, 3, 4, 5, 6 };
        double lastHitTime, endTime;
        int digitCount;

        public void ResetDisplay()
        {
            NoteIndex = -1; Remaining = 0; IsPopped = false;
            visuals.alpha = 0;
            gameObject.SetActive(false);
        }

        public void RecordHit(int index, int total, int hits, double endsAt, double time)
        {
            NoteIndex = index;
            endTime = endsAt;
            lastHitTime = time;
            Remaining = Math.Max(0, total - hits);
            IsPopped = total > 0 && hits >= total;
            int step = total > 0 ? (int)Math.Min(5, (long)hits * 6 / total) : 0;
            body.sprite = bodyFrames[IsPopped ? 7 : InflationFrames[step]];
            SetNumber(Remaining);
            gameObject.SetActive(true);
            ShowTime(time);
        }

        void SetNumber(int remaining)
        {
            string text = remaining.ToString(CultureInfo.InvariantCulture);
            digitCount = text.Length;
            while (digits.Count < digitCount)
            {
                var rect = new GameObject("Digit" + digits.Count, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(number, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.raycastTarget = false;
                digits.Add(image);
            }
            // Nijiiro uses 64 units of advance for its 77-unit-wide glyphs.
            float advance = digitSprites[0].rect.width * (64f / 77f);
            for (int i = 0; i < digits.Count; i++)
            {
                digits[i].gameObject.SetActive(i < digitCount);
                if (i >= digitCount) continue;
                digits[i].sprite = digitSprites[text[i] - '0'];
                digits[i].rectTransform.sizeDelta = digits[i].sprite.rect.size;
                digits[i].rectTransform.anchoredPosition = new Vector2(-digitCount * advance / 2 + i * advance, 0);
            }
        }

        public void ShowTime(double time)
        {
            if (!IsVisible) return;
            double elapsed = Math.Max(0, time - lastHitTime);
            if ((!IsPopped && time > endTime) || (IsPopped && elapsed >= PopFadeSeconds))
            {
                ResetDisplay();
                return;
            }
            visuals.alpha = IsPopped ? 1 - (float)(elapsed / PopFadeSeconds) : 1;
            // TextStretchAnimation, id 6: 50ms rise, then 116ms of stepped return.
            double ms = elapsed * 1000;
            float stretch = ms <= 50 ? 2 + 5 * ((int)ms / 25f)
                : ms <= 166 ? 12 - 2 * ((int)((ms - 50) / 16.57) + 1) : 0;
            for (int i = 0; i < digitCount; i++)
            {
                var rect = digits[i].rectTransform;
                Vector2 size = digits[i].sprite.rect.size;
                float offset = stretch * size.y / 90f;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, offset);
                rect.sizeDelta = new Vector2(size.x, size.y + offset);
            }
        }
    }
}
