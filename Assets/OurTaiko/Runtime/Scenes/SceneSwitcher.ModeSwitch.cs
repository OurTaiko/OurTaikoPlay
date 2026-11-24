using UnityEngine;

namespace OurTaiko
{
    // The play scenes' switch between normal play and practice (PlayScene.ModeSwitch.cs): a new
    // single-player scene for the same song, with where normal play left off handed to practice once.
    public sealed partial class SceneSwitcher
    {
        // The selected request's hand-over (PlayRequest.PracticeEntry), until practice takes it: a
        // restart and any other request start normally.
        (double Position, BranchRoute Branch)? practiceEntry;

        // Practice opens at the measure normal play left off, on the route the lane showed there.
        public bool SwitchToPractice(double position, BranchRoute branch) => SwitchPlayMode(true, position, branch);
        // Normal play always starts over.
        public bool SwitchToNormalPlay() => SwitchPlayMode(false, null, BranchRoute.Normal);

        // Loaded like Play() (SongLoadingScene parses and decodes under the curtain), for the same song
        // and course. Only from Play.Single or Play.Practice: Play.Double (two players, or a ghost
        // battle) never switches, as a single-player scene would drop its second lane.
        bool SwitchPlayMode(bool practice, double? entryPosition, BranchRoute entryBranch)
        {
            if (IsInputBlocked || Request?.Song == null || PracticeMode == practice
                || (CurrentScene != GameScene && CurrentScene != PracticeScene)
                || !Application.CanStreamedLevelBeLoaded(practice ? PracticeScene : GameScene)) return false;
            PracticeMode = practice;
            Select(Request.ForModeSwitch(entryPosition, entryBranch));
            ResetPendingPlay();
            InputManager.DropDelayed();
            LoadSelectedPlay();
            return true;
        }

        // Only the incoming practice scene consumes this; another song must start normally.
        public bool TakePracticeEntry(out double position, out BranchRoute branch)
        {
            position = practiceEntry?.Position ?? 0;
            branch = practiceEntry?.Branch ?? BranchRoute.Normal;
            bool pending = PracticeMode && practiceEntry.HasValue;
            practiceEntry = null;
            return pending;
        }
    }
}
