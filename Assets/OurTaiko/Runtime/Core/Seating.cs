namespace OurTaiko
{
    // Which side a lone player sits on. The single-player code calls the player seat 0 throughout
    // (lane, course, menus); a player who joined alone with the 2P drum sits on the 2P side, so
    // that logical seat 0 is the 2P seat: the 2P drum (InputManager), the 2P options
    // (PlayOptions.ForSeat), the 2P sign-in, nameplate and uploads, and the 2P side's chart block
    // (SeatBlocks, alone). Two players always sit as they are. Entry seats the players of each credit.
    public static class Seating
    {
        // The side of a lone player: 0 (1P) or 1 (2P). 0 while two play.
        public static int Lone { get; private set; }

        public static void SitAlone(int seat) => Lone = seat == 1 ? 1 : 0;
        public static void SitTogether() => Lone = 0;

        // The physical seat (drum, options, account, nameplate) of a logical one.
        public static int Physical(int seat) => Lone == 1 ? 1 - seat : seat;
        public static string Side(int seat) => SeatBlocks.Seats[Physical(seat)];

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Lone = 0;
    }
}
