using UnityEngine;

namespace OurTaiko
{
    // Song-select state kept across scene loads, for both local and server folders.
    public static class SongSelectManager
    {
        // The folder song select had open, reopened when it comes back from a song; ServerLogin
        // closes every folder again (Reset).
        public static string OpenFolderKey { get; set; }

        public static void Reset() => OpenFolderKey = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();
    }
}
