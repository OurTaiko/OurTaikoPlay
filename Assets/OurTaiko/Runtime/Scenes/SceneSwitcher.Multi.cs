namespace OurTaiko
{
    // One round of マルチ演奏: the room's song on the course this player took, played alone in
    // Play.Single with the others' scores beside the lane (MultiMode).
    public sealed class MultiMatch
    {
        // The room's round, so the play knows which start signal is its own.
        public int Round;
        public string Course = "Oni";
    }

    public sealed partial class SceneSwitcher
    {
        public const string MultiRoomScene = "Multi.Room";

        // The round being played; Select clears it, so it is set after Select.
        public MultiMatch Multi { get; private set; }
        public bool PlaysMulti => Multi != null && !PracticeMode;

        // Song select is open for the player to pick the room's song: deciding on a song tells the
        // room instead of playing it, and every way out leads back to the room.
        public bool MultiPick { get; set; }
        // Where song select's own exits lead.
        public string SongSelectExit => MultiPick ? MultiRoomScene : EntryScene;

        // Loads the room's song for this player. The play then waits for the room's start signal,
        // and its result returns to the room.
        public bool PlayMulti(SongDefinition song, string course, int round)
        {
            if (IsInputBlocked || song == null) return false;
            SeatLeadAlone(); PracticeMode = false; MultiPick = false;
            Select(song, course, false);
            Multi = new MultiMatch { Round = round, Course = course };
            ResetPendingPlay();
            ReturnScene = MultiRoomScene;
            LoadSelectedPlay();
            return true;
        }
    }
}
