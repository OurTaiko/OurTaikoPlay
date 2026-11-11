namespace OurTaiko
{
    // ゴーストバトル (issue #53): the player against someone's recorded play of the same block. It
    // plays in DoublePlayScene: the player in the lane of their side (SceneSwitcher.Lead: 1P above, a lone 2P
    // below) and the recording striking the other lane (PlayScene.Ghost.cs).
    public sealed class GhostMatch
    {
        public Online.PlayRecord Record;
        // The recorded play's block; both lanes play it.
        public string Course = "Oni", Side = "single";
        public string Nickname = "", ScoreId = "", Server = "";
        public long Score;
        // The lane the player takes: 0 above (1P), 1 below (2P).
        public int PlayerLane => SceneSwitcher.EnsureInstance().Lead.Index();
        public int GhostLane => 1 - PlayerLane;
    }

    public sealed partial class SceneSwitcher
    {
        public const string GhostSelectScene = "GhostSelect";

        // The match being played; Select clears it, so it is set after Select and stays for restarts.
        public GhostMatch Ghost { get; private set; }
        public bool PlaysGhost => Ghost != null && !PracticeMode;

        // Plays `song` against the recorded play: both seats on its block, the player's own drum
        // and offsets, the recording's options (PlayScene.Ghost.cs). Not 2P's course selection.
        public bool PlayGhost(SongDefinition song, GhostMatch match)
        {
            if (IsInputBlocked || song == null || match?.Record == null) return false;
            SeatLeadAlone(); PracticeMode = false;
            Select(song, match.Course, false);
            SelectedCourse2P = match.Course; AutoPlay2P = false;
            SelectedSide = SelectedSide2P = match.Side;
            Ghost = match;
            ResetPendingPlay();
            if (!IsPlayScene(CurrentScene) && CurrentScene != ResultScene && CurrentScene != SongLoadingScene && !string.IsNullOrEmpty(CurrentScene))
                ReturnScene = CurrentScene;
            LoadSelectedPlay();
            return true;
        }
    }
}
