namespace OurTaiko
{
    // The two sides of the cabinet.
    public enum Side { P1, P2 }

    public static class Sides
    {
        // The side's place in what is kept per seat (the drums, options, accounts, lanes): 1P's
        // first, 2P's second.
        public static int Index(this Side side) => side == Side.P2 ? 1 : 0;
    }

    // The side the first player sits on: a lone player's own side (2P for a player who joined alone
    // with the 2P drum on Entry), and 1P when two play, each on their own side. A lone 2P is 2P
    // everywhere: their own drum (InputManager.Player; 1P's drum does not play for them), the 2P
    // options, sign-in, nameplate and uploads, and the 2P side's chart block. Entry seats the players
    // of each credit.
    public static class Seating
    {
        public static Side Side { get; private set; }

        public static void SitAlone(Side side) => Side = side;
        public static void SitTogether() => Side = Side.P1;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Side = Side.P1;
    }
}
