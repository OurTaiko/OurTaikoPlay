using System;
using UnityEngine;

namespace OurTaiko
{
    public sealed class SoulGaugeView : MonoBehaviour
    {
        [Serializable]
        public sealed class Style
        {
            public Sprite border, empty, grid;
            public Sprite[] rainbow;
            public float clearLabelX;
        }

        public Style[] styles;
        public UnityEngine.UI.Image border, empty, red, clearCap, goldTop, goldBottom;
        public UnityEngine.UI.Image rainbowA, rainbowB, cellFade, grid, clearLabel, soul, fire, soulOverlay;
        public Sprite redFade, capFade, goldFade, clearLit, clearDark, soulLit, soulDark;
        public Sprite[] fireFrames;

        public const int Cells = 50, CellWidth = 21;
        public const double CellFadeSeconds = 0.450, RainbowFrameSeconds = 0.075;
        public bool IsClear => value >= threshold;
        public bool IsFull => value >= 1;
        public int FilledCells => (int)Math.Floor(value * Cells);
        double value, previousValue, threshold, cellChangedAt = double.NegativeInfinity;
        double rainbowStartedAt = double.NaN;
        Style style;

        public void Initialize(double clearThreshold)
        {
            threshold = clearThreshold;
            style = styles[threshold < 0.7 ? 0 : threshold < 0.8 ? 1 : 2];
            border.sprite = style.border;
            empty.sprite = style.empty;
            grid.sprite = style.grid;
            clearLabel.rectTransform.anchoredPosition = new Vector2(style.clearLabelX, 74);
            value = previousValue = 0;
            cellChangedAt = double.NegativeInfinity;
            rainbowStartedAt = double.NaN;
            ShowTime(0);
        }

        public void SetValue(double next, double time)
        {
            next = Math.Max(0, Math.Min(1, next));
            if (next != value)
            {
                previousValue = value;
                cellChangedAt = next > value ? time : double.NegativeInfinity;
                value = next;
            }
            if (!IsFull) rainbowStartedAt = double.NaN;
            else if (double.IsNaN(rainbowStartedAt)) rainbowStartedAt = time;
            ShowTime(time);
        }

        public void ShowTime(double time)
        {
            if (style == null) return;
            int length = FilledCells;
            int clearCell = (int)Math.Round(threshold * Cells);
            float cellAlpha = Mathf.Clamp01((float)((time - cellChangedAt) / CellFadeSeconds));
            bool pending = length > (int)Math.Floor(previousValue * Cells) && cellAlpha < 1;
            int solid = pending ? length - 1 : length;

            // Same 50-cell grid and rounded first gold cell as Gauge::draw().
            Width(red, Math.Min(solid, clearCell - 1) * CellWidth);
            clearCap.enabled = IsClear && !(pending && length == clearCell);
            clearCap.rectTransform.anchoredPosition = new Vector2(738 + (clearCell - 1) * CellWidth, 63);
            int goldWidth = Math.Max(0, solid - clearCell) * CellWidth;
            Width(goldTop, goldWidth);
            Width(goldBottom, goldWidth);
            goldTop.rectTransform.anchoredPosition = new Vector2(738 + clearCell * CellWidth, 60);
            goldBottom.rectTransform.anchoredPosition = new Vector2(738 + clearCell * CellWidth, 26);

            rainbowA.enabled = rainbowB.enabled = IsFull;
            if (IsFull)
            {
                double elapsed = Math.Max(0, time - rainbowStartedAt);
                double frame = elapsed / RainbowFrameSeconds % 8;
                int first = (int)frame;
                float fade = Mathf.Clamp01((float)(elapsed / 0.450));
                rainbowA.sprite = style.rainbow[first];
                rainbowB.sprite = style.rainbow[(first + 1) % 8];
                Alpha(rainbowA, fade);
                Alpha(rainbowB, fade * (float)(frame - first));
            }

            // Nijiiro enables gauge_cell_fade_in: only the newly filled cell fades in.
            // There is no fixed-interval whole-bar yellow flash in the reference code.
            cellFade.enabled = pending;
            if (pending)
            {
                cellFade.sprite = length == clearCell ? capFade : length > clearCell ? goldFade : redFade;
                cellFade.rectTransform.anchoredPosition = new Vector2(738 + (length - 1) * CellWidth, length >= clearCell ? 60 : 27);
                cellFade.rectTransform.sizeDelta = cellFade.sprite.rect.size;
                Alpha(cellFade, cellAlpha);
            }
            Alpha(grid, 0.15f);
            clearLabel.sprite = IsClear ? clearLit : clearDark;
            soul.sprite = IsClear ? soulLit : soulDark;
            int fireFrame = (int)(Math.Max(0, time) / 0.050) % 8;
            fire.enabled = IsFull;
            fire.sprite = fireFrames[fireFrame];
            soulOverlay.enabled = IsFull && (fireFrame == 0 || fireFrame == 1 || fireFrame == 4 || fireFrame == 5);
            Alpha(soulOverlay, 0.5f);
        }

        static void Width(UnityEngine.UI.Image image, int width)
        {
            image.enabled = width > 0;
            var size = image.rectTransform.sizeDelta;
            image.rectTransform.sizeDelta = new Vector2(Math.Max(0, width), size.y);
        }
        static void Alpha(UnityEngine.UI.Image image, float alpha) => image.color = new Color(1, 1, 1, alpha);
    }
}
