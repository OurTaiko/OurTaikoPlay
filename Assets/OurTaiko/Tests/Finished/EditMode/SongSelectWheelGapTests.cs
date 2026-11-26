using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The gap around the open song board follows how far the boards are open, so moving the focus
    // never closes the rows in on a board that is still closing.
    public sealed class SongSelectWheelGapTests
    {
        const double Gap = 120;

        [Test]
        public void OpenFocusedBoardPushesBothNeighboursAway()
        {
            var gaps = new List<(double At, double Weight)> { (540, SongSelectScene.FocusedGap(1, 0, 1)) };
            Assert.That(SongSelectScene.GapShift(405, gaps, Gap), Is.EqualTo(-120));
            Assert.That(SongSelectScene.GapShift(540, gaps, Gap), Is.EqualTo(0));
            Assert.That(SongSelectScene.GapShift(675, gaps, Gap), Is.EqualTo(120));
        }

        [Test]
        public void FocusedBoardKeepsItsGapWhileWaitingToOpen()
        {
            // During the pre-growth hold the new focus is still closed but already has its room.
            Assert.That(SongSelectScene.FocusedGap(0, 0, 1), Is.EqualTo(1));
        }

        [Test]
        public void ClosingBoardKeepsItsGapUntilItHasShrunk()
        {
            // Right after a ka the previous board is still fully open: the new focus gets no room yet.
            Assert.That(SongSelectScene.FocusedGap(0, 1, 1), Is.EqualTo(0));
            // Half closed: the gap is split, and the rows between the two boards keep the whole gap.
            double focused = SongSelectScene.FocusedGap(0, 0.5, 1);
            Assert.That(focused, Is.EqualTo(0.5));
            var gaps = new List<(double At, double Weight)> { (475, 0.5), (540, focused) };
            Assert.That(SongSelectScene.GapShift(400, gaps, Gap), Is.EqualTo(-120));
            Assert.That(SongSelectScene.GapShift(700, gaps, Gap), Is.EqualTo(120));
            Assert.That(SongSelectScene.GapShift(475, gaps, Gap) - SongSelectScene.GapShift(540, gaps, Gap), Is.EqualTo(-120));
        }

        [Test]
        public void SwipeLetsTheReservedGapGo()
        {
            Assert.That(SongSelectScene.FocusedGap(0, 0, 0), Is.EqualTo(0));
            // A board still closing under the swipe keeps its room.
            Assert.That(SongSelectScene.FocusedGap(0.4, 0, 0), Is.EqualTo(0.4));
        }

        // A board leaving part-way open shrinks from its own size instead of snapping fully open.
        [Test]
        public void ClosingStartsFromHowFarTheBoardIsOpen()
        {
            var clip = LumenClip.Parse(File.ReadAllText("Assets/OurTaiko/Animations/song_board.txt"));
            double from = clip.Label("select_off").GetValueOrDefault(30), span = 13;
            // Fully open: select_off from its start (the board holds open, then shrinks).
            Assert.That(SongSelectScene.CloseFrame(clip, from, span, 1), Is.EqualTo(from));
            // Half open: on the shrinking part, at a frame no more open than it is.
            double half = SongSelectScene.CloseFrame(clip, from, span, 0.5);
            Assert.That(half, Is.GreaterThan(from + 3));
            Assert.That(SongSelectScene.OpenAmount(clip, half), Is.LessThanOrEqualTo(0.5));
            Assert.That(SongSelectScene.OpenAmount(clip, half - 0.25), Is.GreaterThan(0.5));
        }
    }
}
