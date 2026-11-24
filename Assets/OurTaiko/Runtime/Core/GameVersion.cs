using UnityEngine;

namespace OurTaiko
{
    // The game's version as it is told to people and servers: {version}-{build number}, e.g.
    // 1.0.2-1234, the build number being the commit count (CI's "display" version).
    //
    // CI builds the desktop and Android players with that whole string as their version, so it is
    // Application.version there. iOS needs plain dotted numbers in its version
    // (CFBundleShortVersionString), so CI gives it the bare one and PlayerBuilds puts the number in
    // CFBundleVersion (every iOS build it makes), which is read back here. The Editor and a desktop
    // or Android build from the menu have no number: their version is the bare one.
    public static class GameVersion
    {
        // Read on the main thread only (Unity's Application.version).
        public static string Full
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return Compose(Application.version, OurTaikoBundle_BuildNumber());
#else
                return Application.version;
#endif
            }
        }

        // {version}-{build}, or the version alone when there is no build number or it already has one.
        public static string Compose(string version, string build)
        {
            version ??= "";
            build = (build ?? "").Trim();
            if (build.Length == 0 || version.Contains("-")) return version;
            return version + "-" + build;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string OurTaikoBundle_BuildNumber();
#endif
    }
}
