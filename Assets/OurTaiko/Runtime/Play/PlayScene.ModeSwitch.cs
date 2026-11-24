using System;

namespace OurTaiko
{
    // The pause menu's "Switch to Practice" / "Switch to Normal Play". A mode change is a new
    // single-player scene through SceneSwitcher.SwitchToPractice / SwitchToNormalPlay, never a live session with a flipped
    // flag: normal play hands over where it was, and practice opens at that measure on the branch it
    // showed (PracticeMode takes it); normal play always starts over. PlayScene calls AddModeSwitch and
    // nothing else of this.
    public sealed partial class PlayScene
    {
        bool switchingMode;

        // After the chart loaded. Two players never switch, as the switch would drop 2P (Entry keeps 2P
        // out of practice for the same reason), nor where the mode does not offer it
        // (PlayMode.OffersModeSwitch: a replay, a ghost battle, practice on a chart missing #E/#M).
        // Where it is not offered the button stays greyed and has no action.
        void AddModeSwitch()
        {
            if (switcher.Seated.Count == 2 || !mode.OffersModeSwitch) return;
            pauseMenu.modeSwitch.onClick.AddListener(SwitchMode);
            pauseMenu.OfferModeSwitch(IsPractice);
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
