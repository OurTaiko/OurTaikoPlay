using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ItemKind = OurTaiko.SongSelectManager.ItemKind;

namespace OurTaiko.Tests
{
    // The play backdrop in a real play: its bands loop from the start, and crossing the clear line
    // brings each band's clear clip in over them, with the fifth dancer.
    public sealed class StageFlowTests
    {
        // 32 notes in two measures, then nothing for a long while: an autoplayed full combo is over
        // the clear line with the song still running.
        static string Chart(string course) =>
            $"TITLE:Stage\nBPM:240\nCOURSE:{course}\nLEVEL:1\n#START\n1111111111111111,\n1111111111111111,\n" + string.Concat(Enumerable.Repeat("0,\n", 60)) + "#END";

        [UnityTest]
        public IEnumerator TheBandsChangeWhenThePlayerClears()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("SCENEPRESET:TOHO\n" + Chart("Oni"));
            try
            {
                TestStage.Require("TOHO");
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                yield return TestPlay.Open(song, autoPlay: true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                Assert.That(Bands(play).Select(b => b.name), Is.EqualTo(new[] { "StageUpper", "StageLower" }));
                Assert.That((play.stage.Choice.Preset, play.stage.Choice.Header, play.stage.Choice.Scene, play.stage.Choice.Troupe), Is.EqualTo(("TOHO", "toho", "toho", "toho")),
                    "The chart names its backdrop.");
                yield return Until(() => Bands(play).All(b => b.Seconds > 0.3), 5);
                Assert.That(play.Lane1.Shown.IsClear, Is.False);
                Assert.That(Bands(play).Select(b => (b.ClearAlpha, b.Clear.RunCount)), Has.All.EqualTo((0f, 0)), "Only the everyday clips before the clear.");
                Assert.That(Bands(play).Select(b => b.Layers[0].RunCount), Has.All.EqualTo(1));
                TestCapture.Capture("StageNormal.png");

                yield return Until(() => play.Lane1.Shown.IsClear, 15);
                yield return Until(() => Bands(play).All(b => b.ClearAlpha >= 1), 2);
                Assert.That(Bands(play).Select(b => b.ClearSeconds), Has.All.InRange(0.2, 1.5), "The clear clips started when the gauge crossed the line.");
                // The clear scene is in, and the fifth dancer has come.
                yield return Until(() => play.stage.scene.ClearSeconds > 2 && play.stage.dancers.Troupe.Active == 5
                    && Enumerable.Range(0, 5).All(slot => play.stage.dancers.Troupe.FrameOf(slot) >= 80), 10);
                Assert.That(Bands(play).Select(b => b.Clear.RunCount), Has.All.GreaterThan(0));
                // Every dancer is drawn, each over the whole stage (its clip says where it stands).
                var drawn = play.stage.dancers.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>();
                Assert.That(drawn.Length, Is.EqualTo(5));
                Assert.That(drawn.Select(g => g.rectTransform.rect.size), Has.All.EqualTo(new Vector2(1920, 1080)));
                TestCapture.Capture("StageClear.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        // Each header follows its own player: 1P clears here, 2P (who plays nothing) does not.
        [UnityTest]
        public IEnumerator EachPlayersHeaderFollowsItsOwnGauge()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("SCENEPRESET:MIKU\n" + Chart("Oni"));
            try
            {
                TestStage.Require("MIKU");
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                while (SceneSwitcher.Instance.IsInputBlocked) yield return null;
                SceneSwitcher.Instance.SetSeats(Side.P1, Side.P2);
                yield return TestPlay.Open(song, "Oni", true, "Oni", false);
                yield return WaitForScene(SceneSwitcher.DoublePlayScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                Assert.That(play.stage.headers.Select(b => (b.name, b.player, b.Layers[0].clip)), Is.EqualTo(new[] { ("StageUpper", 0, "normal"), ("StageUpper2P", 1, "normal") }),
                    "Both players have the same header.");
                yield return Until(() => play.Lanes[0].Shown.IsClear && play.stage.headers[0].ClearAlpha >= 1, 15);
                Assert.That(play.Lanes[1].Shown.IsClear, Is.False);
                Assert.That((play.stage.headers[1].ClearAlpha, play.stage.headers[1].Clear.RunCount, play.stage.headers[1].Layers[0].RunCount), Is.EqualTo((0f, 0, 1)));
                TestCapture.Capture("StageTwoPlayers.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        // A chart that names a backdrop the catalog has gets that backdrop: here the dojo's, which has
        // its own header and no dancers.
        [UnityTest]
        public IEnumerator AChartsScenePresetPicksItsBackdrop()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("SCENEPRESET:dan\n" + Chart("Oni"));
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                yield return TestPlay.Open(song, autoPlay: true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                var choice = play.stage.Choice;
                Assert.That((choice.Preset, choice.Header, choice.Scene, choice.Troupe), Is.EqualTo(("DAN", "dojo", "dojo", (string)null)));
                yield return Until(() => play.stage.headers[0].Seconds > 0.3, 5);
                Assert.That(play.stage.headers[0].Layers.Select(l => l.clip), Is.EqualTo(new[] { "normal_back", "normal_mid_b", "normal_mid_c", "normal_petals", "normal_front" }));
                Assert.That(play.stage.headers[0].Layers.Where(l => l.clip != "normal_petals").Select(l => l.RunCount), Has.All.EqualTo(1));
                Assert.That(play.stage.dancers.Troupe, Is.Null, "This backdrop has no dancers.");
                Assert.That(play.stage.scene.Clear, Is.Null, "Nor a clear scene.");
                TestCapture.Capture("StagePreset.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        // A local song in a box.def folder that names a backdrop plays on it; its own SCENEPRESET
        // comes first.
        [UnityTest]
        public IEnumerator AFoldersScenePresetIsForTheSongsOpenedFromIt()
        {
            string root = System.IO.Path.Combine(Application.temporaryCachePath, "stage-songs-" + System.Guid.NewGuid().ToString("N"));
            void Write(string relative, string text)
            {
                string path = System.IO.Path.Combine(root, relative);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, text);
            }
            Write("Outer/box.def", "#TITLE:Outer\n#SCENEPRESET:MIKU\n");
            Write("Outer/Inner/box.def", "#TITLE:Inner\n#SCENEPRESET:TOHO\n");
            Write("Outer/Inner/a.tja", Chart("Oni"));
            Write("Outer/Inner/b.tja", "SCENEPRESET:GUMI\n" + Chart("Oni"));
            Write("Outer/Inner/c.tja", "SCENEPRESET:no such backdrop\n" + Chart("Oni"));
            try
            {
                TestStage.Require("TOHO");
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                TestData.UseServers();
                LocalSongLibrary.EnsureInstance().UseRoot(root);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
                yield return null;
                var manager = Object.FindAnyObjectByType<SongSelectScene>().Manager;
                int Board(string title) => Enumerable.Range(0, manager.Items.Count)
                    .First(i => manager.Items[i].Kind == ItemKind.Folder && manager.Folders[manager.Items[i].Folder].Title == title);
                // Local, then Outer inside it, then Inner inside that.
                foreach (string title in new[] { SongSelectManager.LocalTitle, "Outer", "Inner" })
                {
                    manager.OpenFolderAt(Board(title));
                    yield return null;
                }
                var boards = manager.Items.Where(i => i.Kind == ItemKind.Song).ToArray();
                Assert.That(boards.Length, Is.EqualTo(3));
                Assert.That(boards.Select(manager.ScenePresetsOf), Has.All.EqualTo(new[] { "TOHO", "MIKU" }),
                    "The folders a song was opened from, the nearest first; Local names none.");
                var third = boards.First(b => b.Info.ScenePreset == "no such backdrop");
                var switcher = SceneSwitcher.Instance;
                while (switcher.IsInputBlocked) yield return null;
                switcher.Select(third.Song, null, true, null, false, manager.ScenePresetsOf(third));
                Assert.That(switcher.ScenePresets(third.Info.ScenePreset), Is.EqualTo(new[] { "no such backdrop", "TOHO", "MIKU" }), "The chart's own counts most.");
                yield return SceneManager.LoadSceneAsync(switcher.SelectedPlayScene);
                yield return WaitForScene(SceneSwitcher.GameScene);
                // A play that is not opened from a folder has only the chart's.
                Assert.That(switcher.ScenePresets("GUMI"), Has.Length.EqualTo(3));
                var play = Object.FindAnyObjectByType<PlayScene>();
                Assert.That((play.stage.Choice.Preset, play.stage.Choice.Header), Is.EqualTo(("TOHO", "toho")),
                    "The chart names a backdrop the game does not have, so its nearest folder's is the one.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                LocalSongLibrary.EnsureInstance().UseRoot(LocalSongLibrary.DefaultRoot);
                System.IO.Directory.Delete(root, true);
            }
        }

        static StageBandView[] Bands(PlayScene play) => new[] { play.stage.headers[0], play.stage.scene };

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done()) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
