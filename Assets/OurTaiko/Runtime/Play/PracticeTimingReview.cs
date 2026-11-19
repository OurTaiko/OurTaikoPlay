using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Practice: while the paused lane is browsed, each note of the attempt just played shows how early
    // or late it was hit ("+12", "-8") or that it was missed ("×"). A lane overlay: the note renderer
    // only says which notes are on screen, and everything about the review lives here.
    public sealed class PracticeTimingReview : INoteLaneOverlay
    {
        // Labels past the warm ones are made a few a frame, so an extreme HS fills in instead of hitching.
        const int WarmLabels = 32, LabelsPerFrame = 2;
        const float RowOffset = 72;
        static readonly Color MissColor = new Color32(200, 200, 200, 255);

        sealed class Label
        {
            public TextMeshPro Text;
            public double? Offset;
            public bool Missed;
        }

        // Read only while a review could show, so the settings overlay can switch it over a paused practice.
        readonly Func<bool> enabled;
        PlaySession attempt;
        Transform parking;
        readonly Stack<Label> free = new Stack<Label>();
        Label[] shown = Array.Empty<Label>();
        bool active, changed, unfinished;
        int budget, budgetFrame = -1;

        public PracticeTimingReview(Func<bool> enabled) => this.enabled = enabled;

        // The attempt to review; null shows nothing. Kept apart from the fresh session the paused
        // lane browses with.
        public PlaySession Attempt
        {
            get => attempt;
            set { if (attempt != value) { attempt = value; changed = true; } }
        }

        public void Prepare(PlaySession session, ScrollClockTimeline clocks, Transform layer)
        {
            attempt = null; active = false;
            shown = new Label[session.Chart.Notes.Count];
            if (parking == null)
            {
                parking = new GameObject("PracticeTimingLabels", typeof(RectTransform)).transform;
                parking.SetParent(layer, false);
            }
            if (!enabled()) return;
            for (int i = free.Count, n = Math.Min(WarmLabels, session.Chart.Notes.Count); i < n; i++)
            {
                var label = NewLabel();
                label.Text.gameObject.SetActive(true);
                label.Text.text = "+0123456789-×";
                label.Text.ForceMeshUpdate(true);
                label.Text.text = "";
                label.Text.gameObject.SetActive(false);
                free.Push(label);
            }
        }

        public bool BeginFrame(PlaySession session, double time, bool preview)
        {
            bool now = preview && attempt != null && enabled();
            bool redraw = now != active || changed || unfinished;
            active = now; changed = unfinished = false;
            if (budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; budget = LabelsPerFrame; }
            return redraw;
        }

        public void ShowNote(int index, ChartNote note, RectTransform root)
        {
            var label = shown[index];
            double? offset = active ? attempt.TimingOffsetMs(index) : null;
            bool missed = active && attempt.Missed[index] && !attempt.Skipped[index];
            if (!offset.HasValue && !missed)
            {
                if (label != null) HideNote(index);
                return;
            }
            if (label == null)
            {
                if (free.Count == 0)
                {
                    if (budget == 0) { unfinished = true; return; }
                    budget--;
                    free.Push(NewLabel());
                }
                label = shown[index] = free.Pop();
                // Under the note's own SortingGroup, so it stacks and moves with the note.
                label.Text.rectTransform.SetParent(root, false);
                label.Text.gameObject.SetActive(true);
            }
            if (label.Offset != offset || label.Missed != missed)
            {
                label.Text.rectTransform.anchoredPosition = new Vector2(0, offset < 0 && !missed ? -RowOffset : RowOffset);
                label.Text.text = missed ? "×" : TimingOffsetText.Format(offset.Value);
                label.Text.color = missed ? MissColor : TimingOffsetText.ColorOf(offset.Value);
                label.Offset = offset;
                label.Missed = missed;
            }
        }

        public void HideNote(int index)
        {
            var label = shown[index];
            if (label == null) return;
            label.Text.gameObject.SetActive(false);
            label.Text.rectTransform.SetParent(parking, false);
            free.Push(label);
            shown[index] = null;
        }

        Label NewLabel()
        {
            var rect = new GameObject("PracticeTiming", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parking, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(100, 32);
            rect.anchoredPosition = new Vector2(0, RowOffset);
            // The font goes on before Awake: a TextMeshPro that wakes without one takes TMP's default
            // font and writes _CullMode on its shared material, dirtying that asset in the Editor.
            rect.gameObject.SetActive(false);
            var text = rect.gameObject.AddComponent<TextMeshPro>();
            text.UseUiFont();
            rect.gameObject.SetActive(true);
            text.isOrthographic = true;
            text.fontSize = 26;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.GetComponent<MeshRenderer>().sortingOrder = 3;
            rect.gameObject.SetActive(false);
            return new Label { Text = text };
        }
    }
}
