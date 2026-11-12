using System.Collections;
using UnityEngine.SceneManagement;

namespace OurTaiko.Tests
{
    // Opens the play scene for a song the way a scene opened directly does: no rainbow curtain and
    // no SongLoadingScene, which take about 2.5 s each time. SongLoadingCurtainTests and the flows
    // that start from the song select cover that route; a test about the play scene itself opens it
    // here, with the same arguments as SceneSwitcher.Play.
    public static class TestPlay
    {
        public static IEnumerator Open(SongDefinition song, bool autoPlay = false) => Open(song, null, autoPlay);

        public static IEnumerator Open(SongDefinition song, string course, bool autoPlay, string course2P = null, bool autoPlay2P = false)
        {
            var switcher = SceneSwitcher.EnsureInstance();
            switcher.Select(song, course, autoPlay, course2P, autoPlay2P);
            yield return SceneManager.LoadSceneAsync(switcher.SelectedPlayScene);
            yield return null;
        }
    }
}
