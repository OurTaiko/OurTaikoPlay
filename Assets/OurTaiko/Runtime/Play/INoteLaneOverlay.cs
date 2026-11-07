using UnityEngine;

namespace OurTaiko
{
    // Something a mode draws on the notes of a lane (the practice timing review) without the note
    // renderer knowing about it. The renderer owns the note views and when they are drawn; an overlay
    // is told which notes are on screen and hangs its own objects under their roots. A lane with no
    // overlays pays nothing.
    public interface INoteLaneOverlay
    {
        // A chart is put on the lane (again for each song of a 段位 course). Every note was hidden
        // first. `layer` is the note layer, a parent for objects the overlay keeps off the lane.
        void Prepare(PlaySession session, ScrollClockTimeline clocks, Transform layer);
        // Before each Render. True draws the frame even when the lane itself has not changed (the
        // overlay was switched, or work is left from the frame before).
        bool BeginFrame(PlaySession session, double time, bool preview);
        // Note `index` is drawn this frame at `root` (a pooled view: another note may own it later).
        void ShowNote(int index, ChartNote note, RectTransform root);
        // Note `index` left the lane; its root goes back to the pool.
        void HideNote(int index);
    }
}
