namespace OurTaiko
{
    public sealed partial class SceneSwitcher
    {
        public const string MultiRoomScene = "Multi.Room";

        // Song select is open for the player to pick the room's song: deciding on a song tells the
        // room instead of playing it, and every way out leads back to the room.
        public bool MultiPick { get; set; }
        // Where song select's own exits lead.
        public string SongSelectExit => MultiPick ? MultiRoomScene : EntryScene;
    }
}
