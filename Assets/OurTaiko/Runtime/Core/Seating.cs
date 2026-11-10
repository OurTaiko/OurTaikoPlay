namespace OurTaiko
{
    // The side a lone player sits on: 0 for 1P, 1 for 2P (a player who joined alone with the 2P
    // drum on Entry). With two players each sits on their own side and this is 0. A lone 2P is 2P
    // everywhere: their own drum (InputManager.Player; 1P's drum does not play for them), the 2P
    // options, sign-in, nameplate and uploads, and the 2P side's chart block. Entry seats the players
    // of each credit.
    public static class Seating
    {
        public static int Side { get; private set; }

        public static void SitAlone(int side) => Side = side == 1 ? 1 : 0;
        public static void SitTogether() => Side = 0;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Side = 0;
    }
}
