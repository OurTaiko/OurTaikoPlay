using System;

namespace OurTaiko.Chara
{
    // Each seat's Don: the look its account chose on the website once DonDownloads has it, else
    // the plain Don in the seat's colours (a guest's).
    public static class DonLooks
    {
        public static DonLook For(int seat) => DonDownloads.For(Math.Clamp(seat, 0, 1));
    }
}
