using System;
using System.Linq;

namespace OurTaiko
{
    // The pause menu's "Switch to Practice" / "Switch to Normal Play". A mode change is a new
    // single-player scene through SceneSwitcher.SwitchToPractice / SwitchToNormalPlay, never a live session with a flipped
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

        // After the chart loaded. Two players never switch: the switch would have to drop the joined 2P
        // (Entry keeps 2P out of practice for the same reason). A chart missing #E/#M only plays on a
        // fixed route, so practice does not offer normal play for it (PlaySession would refuse).
        // Where it is not offered the button stays greyed and has no action.
        void AddModeSwitch()
        {
            if (IsTwoPlayer || switcher.TwoPlayers || IsPractice && !Session.Chart.Branches.All(b => b.HasAllRoutes)) return;
            pauseMenu.modeSwitch.onClick.AddListener(SwitchMode);
            pauseMenu.OfferModeSwitch(IsPractice);
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
            // Only AddModeSwitch wires this, so two players and practice-only charts never get here.
            if (!IsPaused || closingPauseMenu || switcher.IsInputBlocked || IsFinished || switchingMode || !pausePanel.activeSelf) return;
            switchingMode = true;
            Func<bool> load;
            if (IsPractice) load = switcher.SwitchToNormalPlay;
            else
            {
                // Normal play runs at speed 1, so the plain offsets give the cursor in visual chart time,
                // as practice reads it. The route is read at that same time, so a pause just inside a
                // branch section hands over that section's measure with its route.
                double position = SongTime - audioOffset - visualOffset;
                var branch = Session.DisplayBranchAt(position);
                load = () => switcher.SwitchToPractice(position, branch);
            }
            StartCoroutine(ClosePauseMenu(() =>
            {
                if (!load())
                {
                    switchingMode = false;
                    pauseMenu.Show();
                }
            }));
        }
    }
}
