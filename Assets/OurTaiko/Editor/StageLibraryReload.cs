using UnityEditor;

namespace OurTaiko.Editor
{
    // StageLibrary keeps what it read from outside the game in statics: the fetched backdrops'
    // asset bundles it opened and the pictures it decoded from the player's folder. A script reload
    // in the Editor empties those statics but unloads nothing, and a bundle still loaded keeps the
    // same file from being opened again: every published backdrop was drawn empty until the Editor
    // restarted. So the library gives everything back before the reload, as it does when it forgets.
    [InitializeOnLoad]
    static class StageLibraryReload
    {
        static StageLibraryReload() => AssemblyReloadEvents.beforeAssemblyReload += StageLibrary.Forget;
    }
}
