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

        // Practice opens at the measure normal play left off, on the route the lane showed there.
        public bool SwitchToPractice(double position, BranchRoute branch) => SwitchPlayMode(true, position, branch);
        // Normal play always starts over.
        public bool SwitchToNormalPlay() => SwitchPlayMode(false, null, BranchRoute.Normal);

        // Loaded like Play() (SongLoadingScene parses and decodes under the curtain), for the same song
        // and course. Only from GameScene or PracticeScene: DoublePlayScene (two players, or a ghost
        // battle) never switches, as a single-player scene would drop its second lane.
        bool SwitchPlayMode(bool practice, double? entryPosition, BranchRoute entryBranch)
        {
            if (IsInputBlocked || SelectedSong == null || PracticeMode == practice
                || (CurrentScene != GameScene && CurrentScene != PracticeScene)
                || !Application.CanStreamedLevelBeLoaded(practice ? PracticeScene : GameScene)) return false;
            PracticeMode = practice;
            Select(SelectedSong, SelectedCourse, AutoPlay);
            ResetPendingPlay();
            // After Select, which clears any earlier hand-over.
            practiceEntryPosition = entryPosition;
            practiceEntryBranch = entryBranch;
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
