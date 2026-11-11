namespace OurTaiko
{
    // The two sides of the cabinet. Who sits on which this credit is SceneSwitcher.Seated.
    public enum Side { P1, P2 }

    public static class Sides
    {
        // The side's place in what is kept per seat (the drums, options, accounts, lanes): 1P's
        // first, 2P's second.
        public static int Index(this Side side) => side == Side.P2 ? 1 : 0;
    }
}
