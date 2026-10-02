using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // ScoreCounter: lane_score_cover, then score_number digits right-aligned at x 255 in the lane. Each
    // change restarts the text stretch (digits grow upwards). Runs on real time like the original's
    // current_ms; Nijiiro's delay_score_addition is off, so the count follows the score at once.
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScoreCounterView : MonoBehaviour
    {
        public Image cover;
        [Tooltip("score_number frames 0-9.")]
        public Sprite[] digits;

        readonly List<Image> images = new List<Image>();
        int score = -1;
        double changedAt = double.NegativeInfinity;

        public string Text { get; private set; } = "";
        public float Stretch { get; private set; }
        public Image Digit(int index) => images[index];

        public void Show(int value)
        {
            if (value == score) return;
            // ScoreCounter starts at 0 without a stretch; later changes restart it.
            if (score >= 0) changedAt = Time.unscaledTimeAsDouble;
            score = value;
            Text = ScoreCounterLayout.Text(value);
            while (images.Count < Text.Length) images.Add(SkinUi.Image("Digit" + images.Count, transform, null));
            for (int i = 0; i < images.Count; i++)
            {
                bool shown = i < Text.Length;
                images[i].enabled = shown;
                if (shown) images[i].sprite = digits[Text[i] - '0'];
            }
            Layout(TextStretch.Pixels((Time.unscaledTimeAsDouble - changedAt) * 1000));
        }

        void Update()
        {
            if (score < 0) return;
            float stretch = TextStretch.Pixels((Time.unscaledTimeAsDouble - changedAt) * 1000);
            if (stretch != Stretch) Layout(stretch);
        }

        void Layout(float stretch)
        {
            Stretch = stretch;
            for (int i = 0; i < Text.Length; i++)
            {
                var rect = images[i].rectTransform;
                rect.sizeDelta = new Vector2(ScoreCounterLayout.DigitWidth, ScoreCounterLayout.DigitHeight + stretch);
                rect.TopLeft(ScoreCounterLayout.DigitLeft(i, Text.Length), ScoreCounterLayout.DigitTop - stretch);
            }
        }
    }
}
