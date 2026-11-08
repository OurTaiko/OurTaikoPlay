using UnityEngine;

namespace OurTaiko
{
    // The play scenes' switch between normal play and practice (PlayScene.ModeSwitch.cs): a new
    // single-player scene for the same song, with where normal play left off handed to practice once.
    public sealed partial class SceneSwitcher
    {
        double? practiceEntryPosition;
        BranchRoute practiceEntryBranch;

        // Select() clears it: another song must start normally.
        void ClearPracticeEntry()
        {
            practiceEntryPosition = null;
            practiceEntryBranch = BranchRoute.Normal;
        }

        // A mode change is a new single-player scene for the same song and course, loaded like Play()
        // (SongLoadingScene parses and decodes under the curtain), never a live session with a flipped
        // flag. Normal play always starts over; only practice takes the handed-over position. A joined
        // 2P is never dropped: Entry keeps 2P out of practice, so this is not reached with two players.
        public bool SwitchPlayMode(bool practice, double position, BranchRoute branch)
        {
            if (IsInputBlocked || SelectedSong == null || PracticeMode == practice || TwoPlayers
                || (CurrentScene != GameScene && CurrentScene != PracticeScene)
                || !Application.CanStreamedLevelBeLoaded(practice ? PracticeScene : GameScene)) return false;
            PracticeMode = practice;
            Select(SelectedSong, SelectedCourse, AutoPlay);
            ResetPendingPlay();
            LastResult = LastResult2P = null;
            if (practice)
            {
                practiceEntryPosition = position;
                practiceEntryBranch = branch;
            }
            InputManager.DropDelayed();
            LoadSelectedPlay();
            return true;
        }

        // Only the incoming practice scene consumes this; another song must start normally.
        public bool TakePracticeEntry(out double position, out BranchRoute branch)
        {
            position = practiceEntryPosition ?? 0;
            branch = practiceEntryBranch;
            bool pending = PracticeMode && practiceEntryPosition.HasValue;
            ClearPracticeEntry();
            return pending;
        }
    }
}
