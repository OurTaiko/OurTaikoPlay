using System.Collections;
using UnityEngine.SceneManagement;

namespace OurTaiko.Tests
{
    // Opens the play scene for a song the way a scene opened directly does: no rainbow curtain and
    // no SongLoadingScene, which take about 2.5 s each time. SongLoadingCurtainTests and the flows
    // that start from the song select cover that route; a test about the play scene itself opens it
    // here, with the same request as SceneSwitcher.Play.
    public static class TestPlay
    {
        public static IEnumerator Open(SongDefinition song, bool autoPlay = false) => Open(new PlayRequest(song, autoPlay: autoPlay));

        public static IEnumerator Open(SongDefinition song, string course, bool autoPlay) => Open(new PlayRequest(song, course, autoPlay));

        // Both seats' course and オート, 1P's first.
        public static IEnumerator Open(SongDefinition song, SeatPlay first, SeatPlay second) => Open(new PlayRequest(song, new[] { first, second }));

        public static IEnumerator Open(PlayRequest request)
        {
            var switcher = SceneSwitcher.EnsureInstance();
            switcher.Select(request);
            yield return SceneManager.LoadSceneAsync(switcher.SelectedPlayScene);
            yield return null;
        }

        // Practice: starts the next attempt from where the paused lane was moved to, confirming each
        // stage of the menu as a player does. Moving the lane (PracticeMode.Move) only previews;
        // the attempt, and the score and effects of the last one, are reset when it starts. A confirm
        // opens the next stage in the same frame, so each waits a frame.
        public static IEnumerator StartPracticeAttempt(PlayScene play)
        {
            while (!play.Practice.ChoosingSpeed)
            {
                play.Practice.Confirm();
                yield return null;
            }
            play.Practice.Confirm();
        }
    }
}
