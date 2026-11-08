using System.Linq;

namespace OurTaiko
{
    // The pause menu's "Switch to Practice" / "Switch to Normal Play" (PR #64). A mode change is a new
    // single-player scene through SceneSwitcher.SwitchPlayMode, never a live session with a flipped
    // flag: normal play hands over where it was, and practice opens at that measure on the branch it
    // showed; normal play always starts over. PlayScene calls the two hooks below and nothing else of this.
    public sealed partial class PlayScene
    {
        bool switchingMode;
        // Where a normal play left off, in visual chart time; the first practice pause snaps to it.
        double? practiceEntryPosition;

        // Before the lanes are made: the route and position a normal play passed into this practice.
        void TakeModeSwitchEntry()
        {
            if (IsPractice && switcher.TakePracticeEntry(out double position, out BranchRoute branch))
            {
                practiceEntryPosition = position;
                practiceBranch = branch;
            }
        }

        // After the chart loaded: two players never switch (Entry keeps a joined 2P out of practice, and
        // the switch must not drop one), and a chart missing #E/#M only plays on a fixed route, so
        // practice does not offer normal play for it (PlaySession would refuse).
        void AddModeSwitch()
        {
            if (!IsTwoPlayer && !switcher.TwoPlayers && (!IsPractice || Session.Chart.Branches.All(b => b.HasAllRoutes)))
                pauseMenu.ConfigureModeSwitch(IsPractice, SwitchMode);
        }

        // The first practice pause's cursor: the handed-over measure once, otherwise the first bar.
        double TakePracticeStart()
        {
            double position = practiceEntryPosition.HasValue ? Practice.SnapToMeasure(practiceEntryPosition.Value) : Practice.First;
            practiceEntryPosition = null;
            return position;
        }

        public void SwitchMode()
        {
            if (switchingMode || closingPauseMenu || switcher.IsInputBlocked || IsFinished || !IsPaused
                || !pausePanel.activeSelf || IsTwoPlayer || Session == null || pauseMenu.ModeSwitchButton == null) return;
            switchingMode = true;
            // The cursor and the route it shows are read at the same time, so a pause just inside a
            // branch section hands over that section's measure with its route.
            double position = SongTime - audioOffset - visualOffset;
            var branch = Session.DisplayBranchAt(position);
            StartCoroutine(ClosePauseMenu(() =>
            {
                if (!switcher.SwitchPlayMode(!IsPractice, position, branch))
                {
                    switchingMode = false;
                    pauseMenu.Show();
                }
            }));
        }
    }
}
