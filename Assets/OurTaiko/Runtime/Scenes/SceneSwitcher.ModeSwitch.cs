using System;
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

        // A mode change is a new single-player scene, never a live session with a flipped flag.
        public bool SwitchPlayMode(bool practice, double position, BranchRoute branch)
        {
            string target = practice ? PracticeScene : GameScene;
            if (IsInputBlocked || SelectedSong == null || PracticeMode == practice
                || (CurrentScene != GameScene && CurrentScene != PracticeScene)
                || !Application.CanStreamedLevelBeLoaded(target)) return false;
            PracticeMode = practice;
            TwoPlayers = false;
            Select(SelectedSong, SelectedCourse, AutoPlay);
            SelectedCourse2P = null; AutoPlay2P = false;
            completedSong = null; completedRecord = completedRecord2P = null;
            LastResult = LastResult2P = null;
            danLoadPending = false;
            Array.Clear(preparedCharts, 0, preparedCharts.Length);
            Array.Clear(preparedSongs, 0, preparedSongs.Length);
            Array.Clear(preparedCourses, 0, preparedCourses.Length);
            if (practice)
            {
                practiceEntryPosition = position;
                practiceEntryBranch = branch;
            }
            InputManager.DropDelayed();
            SwitchScene(target);
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
