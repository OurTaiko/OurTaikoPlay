using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // ゴーストバトル (issue #53): Entry's board opens the list of recorded plays the server keeps; a
    // picked one is raced in DoublePlayScene, the player in the lane of their side and the recording
    // striking the other, both on the recording's block with its options; only the player's play
    // goes up. A player who joined alone with the 2P drum sits below, on 2P's drum and account.
    public sealed class GhostBattleFlowTests
    {
        FanmadeFixture fixture;
        FanmadeFixture.Chart chart;
        GameSettings saved;
        PlayOptions originalFirst, originalSecond;
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        double clock;
        const string Tja = "TITLE:Ghost\nBPM:240\nOFFSET:0\nCOURSE:Oni\nLEVEL:3\n#START\n1111,\n1111,\n#END\n";

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture();
            fixture.Users["ka"] = "katsu";
            chart = new FanmadeFixture.Chart
            {
                Title = "Ghost Song", Subtitle = "Recorded", AudioName = "song.ogg",
                Tja = Encoding.UTF8.GetBytes(Tja), Audio = File.ReadAllBytes("Assets/OurTaiko/Audio/don.ogg"),
            };
            fixture.Charts.Add(chart);
            saved = SettingManager.EnsureInstance().Settings.Clone();
            var settings = new GameSettings();
            settings.play.audioOffsetMs = 7;
            settings.play.judgeOffsetMs = 30;
            SettingManager.EnsureInstance().UseUnsaved(settings);
            originalFirst = PlayOptions.Shared; originalSecond = PlayOptions.Second;
            PlayOptions.Shared = new PlayOptions { speed = 30 }; PlayOptions.Second = new PlayOptions { speed = 35 };
            var input = InputSystem.settings;
            originalBackground = input.backgroundBehavior;
            originalEditorBehavior = input.editorInputBehaviorInPlayMode;
            input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            input.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("GhostKeyboard");
            keyboard.MakeCurrent();
            clock = InputState.currentTime;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("GhostTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            fixture.Dispose();
            TestData.UseServers();
            Seating.SitTogether();
            if (saved != null) SettingManager.EnsureInstance().UseUnsaved(saved);
            PlayOptions.Shared = originalFirst; PlayOptions.Second = originalSecond;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = originalBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        // The rival's play: ×1.5, every note 良 but the fourth, which it left alone.
        PlayRecord Recording()
        {
            var record = new PlayRecord { Speed = 15, AudioOffsetMs = -50, VisualOffsetMs = 20 };
            var notes = TjaParser.Parse(Tja).Notes;
            for (int i = 0; i < notes.Count; i++)
                if (i != 3) record.Inputs.Add((notes[i].Time * 1000, PlayRecord.TypeOf(notes[i].IsKa, i % 2 == 1)));
            return PlayRecord.FromJson(record.ToJson());
        }

        // Entry → (1P or a lone 2P joins) → ゴーストバトル → the list, with the replay listed.
        IEnumerator OpenList(bool lone2P)
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            yield return new WaitForSecondsRealtime(.3f);
            if (lone2P) entry.Join2P(); else entry.Don();
            Assert.That(entry.Lone2P, Is.EqualTo(lone2P));
            yield return Waits.Until(() => entry.Flow.IsModeReady(entry.Now), seconds: 3);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(entry.Modes[3].Scene, Is.EqualTo(SceneSwitcher.GhostSelectScene));
            Assert.That(entry.Modes[3].Title, Is.EqualTo("ゴーストバトル"));
            // Alone, 1P or 2P, every board is open (段位道場 is not greyed).
            Assert.That(entry.view.boards[1].closed.color.r, Is.EqualTo(1));
            entry.Ka(3);
            yield return new WaitForSecondsRealtime(.7f);
            TestCapture.Capture(lone2P ? "EntryGhostBoard2P.png" : "EntryGhostBoard.png");
            // A lone 2P's own don picks the board (1P's would join 1P).
            if (lone2P) entry.Don2P(); else entry.Don();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.GhostSelectScene && !SceneSwitcher.Instance.IsInputBlocked, seconds: 10);
            Assert.That(Seating.Side, Is.EqualTo(lone2P ? 1 : 0));
        }

        static IEnumerator Race(GhostSelectScene list)
        {
            list.Pick();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.DoublePlayScene && !SceneSwitcher.Instance.IsInputBlocked,
                () => "Still in " + SceneManager.GetActiveScene().name + ": " + list.Status, 40);
        }

        [UnityTest]
        public IEnumerator APickedPlayIsRacedToTheEndAndOnlyThePlayerUploads()
        {
            var record = Recording();
            var row = fixture.AddReplay(chart, "Oni", record, "Rival", 978000);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect("don");
            yield return OpenList(false);

            var list = Object.FindAnyObjectByType<GhostSelectScene>();
            yield return Waits.Until(() => list.Rows.Count == 1, () => "Rows: " + list.Status, 10);
            yield return null;
            var shelf = list.view.columns;
            // The shelf: the server (one here) with もどる first, its categories, the plays of the
            // highlighted one (すべて), each column opening with もどる.
            Assert.That(list.Column, Is.Zero);
            Assert.That(list.Entries(0).Select(e => e.Kind), Is.EqualTo(new[] { GhostSelectScene.Kind.Back, GhostSelectScene.Kind.Server }));
            Assert.That(shelf[0].rows[0].title.text, Is.EqualTo(GhostSelectScene.BackLabel));
            Assert.That(shelf[0].rows[1].detail.text, Is.EqualTo("1 件"));
            Assert.That(list.Servers.Single().Categories.Select(c => c.Title), Is.EqualTo(new[] { "すべて", "Game", "Pop" }));
            Assert.That(shelf[1].rows[1].title.text, Is.EqualTo("すべて"));
            Assert.That(shelf[2].rows[1].title.text, Is.EqualTo("Ghost Song"));
            Assert.That(shelf[2].rows[1].detail.text, Does.Contain("Rival").And.Contain("おに ★8"));
            Assert.That(shelf[2].rows[1].score.text, Is.EqualTo("978,000"));
            Assert.That(shelf[2].rows[2].root.gameObject.activeSelf, Is.False);
            TestCapture.Capture("GhostSelect.png");

            // Into the categories: Game holds the song, Pop nothing; each loads when highlighted.
            list.Confirm();
            Assert.That(list.Column, Is.EqualTo(1));
            Assert.That(list.Focused.Category.Id, Is.Null);
            list.Move(1);
            yield return Waits.Until(() => !list.IsLoading, seconds: 10);
            yield return null;
            Assert.That(list.CurrentCategory.Id, Is.EqualTo("game"));
            Assert.That(list.Rows.Single().ScoreId, Is.EqualTo((string)row["id"]));
            list.Move(1);
            yield return Waits.Until(() => !list.IsLoading, seconds: 10);
            yield return null;
            Assert.That(list.CurrentCategory.Id, Is.EqualTo("pop"));
            Assert.That(list.Rows, Is.Empty);
            Assert.That(list.view.status.text, Does.Contain("category"));
            Assert.That(shelf[2].rows[0].title.text, Is.EqualTo(GhostSelectScene.BackLabel), "An empty column keeps its もどる.");
            TestCapture.Capture("GhostSelectEmptyCategory.png");
            // もどる returns a column; a tap on a category selects it, a second tap enters it.
            list.Move(-3);
            Assert.That(list.Focused.Kind, Is.EqualTo(GhostSelectScene.Kind.Back));
            list.Confirm();
            Assert.That(list.Column, Is.Zero);
            list.Back(leave: false);
            Assert.That(list.Column, Is.Zero, "← on the servers stays.");
            list.Tap(1, 1);
            Assert.That((list.Column, list.CurrentCategory.Id), Is.EqualTo((1, (string)null)));
            list.Tap(1, 1);
            Assert.That(list.Column, Is.EqualTo(2));
            Assert.That(list.SelectedRow.ScoreId, Is.EqualTo((string)row["id"]));
            Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/replays"), Is.EqualTo(3), "Each category loads once.");

            yield return Race(list);
            var play = Object.FindAnyObjectByType<PlayScene>();
            Assert.That(play.IsGhost, Is.True);
            Assert.That(play.Lane1.Views, Is.SameAs(play.firstLane), "1P races in the upper lane.");
            Assert.That(play.GhostLane.Views, Is.SameAs(play.secondLane));
            Assert.That(play.GhostLane.Replay, Is.Not.Null);
            Assert.That(play.Lane1.Replay, Is.Null);
            Assert.That(play.Lane1.AutoPlay || play.GhostLane.AutoPlay, Is.False);
            // Both lanes play the recording's block with its options, not the player's ×3.0.
            Assert.That(play.Lane1.Options.speed, Is.EqualTo(15));
            Assert.That(play.GhostLane.Options.speed, Is.EqualTo(15));
            Assert.That(play.GhostLane.Options.neiro, Is.EqualTo(PlayOptions.Mute), "The recording's drum is silent.");
            Assert.That(play.Lane1.Session.Chart.Notes.Count, Is.EqualTo(8));
            Assert.That(play.GhostLane.Session.Chart.Notes.Count, Is.EqualTo(8));
            // The player's offsets are their own (7 ms + the 30 ms judgment offset), not the recording's.
            Assert.That(play.Lane1.Record.AudioOffsetMs, Is.EqualTo(37));
            Assert.That(play.Lane1.Record.Speed, Is.EqualTo(15));
            var plates = Object.FindObjectsByType<NameplateView>(FindObjectsInactive.Include).Where(p => p.gameObject.scene == play.gameObject.scene).ToList();
            Assert.That(plates.Single(p => p.seat == 1).Info.name, Is.EqualTo("Rival"), "The recording's lane wears its player's name.");
            Assert.That(plates.Single(p => p.seat == 0).Info.name, Is.Not.EqualTo("Rival"));

            yield return new WaitForSecondsRealtime(1);
            TestCapture.Capture("GhostPlay.png");
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.ResultScene, seconds: 40);
            var switcher = SceneSwitcher.Instance;
            // The player struck nothing; the recording hit all but the fourth.
            Assert.That((switcher.LastResult.Good, switcher.LastResult.Bad), Is.EqualTo((0, 8)));
            Assert.That((switcher.LastResult2P.Good, switcher.LastResult2P.Ok, switcher.LastResult2P.Bad), Is.EqualTo((7, 0, 1)));
            yield return null;
            var result = Object.FindAnyObjectByType<ResultScene>();
            Assert.That(result.Boards.Count, Is.EqualTo(2));
            Assert.That(result.Boards[1].View.nameplate.Info.name, Is.EqualTo("Rival"), "The recording's player on its board.");
            TestCapture.Capture("GhostResult.png");

            // Only the player's play goes up, on the recording's block, with the player's recording.
            yield return Waits.Until(() => fixture.AcceptedScores.Count == 1, seconds: 10);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(fixture.AcceptedScores.Count, Is.EqualTo(1));
            var sent = fixture.AcceptedScores.Single();
            Assert.That((long)sent["difficultyId"], Is.EqualTo(chart.BlockId("Oni")));
            Assert.That((int)sent["bad"], Is.EqualTo(8));
            Assert.That((int)sent["replay_data"]["options"]["speed"], Is.EqualTo(15));
            Assert.That(fixture.AcceptedUsers.Single(), Is.EqualTo("don"));

            // Leaving the result returns to the list.
            switcher.ReturnToMenu();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.GhostSelectScene, seconds: 10);
        }

        [UnityTest]
        public IEnumerator ALone2PRacesInTheLowerLaneOnTheTwoPDrumAndAccount()
        {
            fixture.AddReplay(chart, "Oni", Recording(), "Rival", 978000);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect();
            var signIn = OnlineManager.Instance.SignInAsync(1, "ka", "katsu");
            yield return Waits.Until(() => signIn.IsCompleted, seconds: 10);
            signIn.GetAwaiter().GetResult();
            yield return OpenList(true);
            // A lone 2P is 2P: their own drum, nothing remapped.
            Assert.That(InputManager.Player, Is.SameAs(InputManager.P2));

            var list = Object.FindAnyObjectByType<GhostSelectScene>();
            yield return Waits.Until(() => list.Rows.Count == 1, () => "Rows: " + list.Status, 10);
            list.Confirm(); list.Confirm();
            yield return Race(list);
            var play = Object.FindAnyObjectByType<PlayScene>();
            Assert.That(play.Lane1.Views, Is.SameAs(play.secondLane), "A lone 2P races in the lower lane.");
            Assert.That(play.GhostLane.Views, Is.SameAs(play.firstLane));
            var plates = Object.FindObjectsByType<NameplateView>(FindObjectsInactive.Include).Where(p => p.gameObject.scene == play.gameObject.scene).ToList();
            Assert.That(plates.Single(p => p.seat == 0).Info.name, Is.EqualTo("Rival"));

            // 1P's drum key does nothing for a lone 2P; 2P's strikes the player's lane.
            yield return Waits.Until(() => play.Lane1.Session != null && play.SongTime > 0.5, seconds: 10);
            Assert.That(play.Lane1.DrumSeat, Is.EqualTo(1));
            var p1Don = InputManager.GetBinding(InputKey.LeftDon)[0];
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(p1Don), clock += 0.001);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(), clock += 0.001);
            yield return null; yield return null;
            Assert.That(play.Lane1.Record.Inputs, Is.Empty, "1P's drum is not the lone 2P's.");
            var p2Don = InputManager.GetBinding(InputKey.P2LeftDon)[0];
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(p2Don), clock += 0.001);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(), clock += 0.001);
            yield return null; yield return null;
            Assert.That(play.Lane1.Record.Inputs.Count, Is.EqualTo(1));
            Assert.That(PlayRecord.IsKa(play.Lane1.Record.Inputs[0].Type), Is.False);

            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.ResultScene, seconds: 40);
            var switcher = SceneSwitcher.Instance;
            Assert.That((switcher.LastResult.Good, switcher.LastResult.Bad), Is.EqualTo((7, 1)), "The recording's result is above.");
            Assert.That(switcher.LastResult2P.Good + switcher.LastResult2P.Ok + switcher.LastResult2P.Bad, Is.EqualTo(8));
            yield return Waits.Until(() => fixture.AcceptedScores.Count == 1, seconds: 10);
            Assert.That(fixture.AcceptedUsers.Single(), Is.EqualTo("ka"), "The lone 2P's play is 2P's account's.");
            // The best a lone 2P sees is their own account's; 1P is a guest and has none.
            var played = switcher.SelectedSong;
            yield return Waits.Until(() => SongScores.Get(played, Difficulty.Oni) != null, seconds: 10);
            Assert.That(SongScores.Get(played, Difficulty.Oni, 0), Is.Null);
            var result = Object.FindAnyObjectByType<ResultScene>();
            Assert.That(result.Boards[0].View.nameplate.Info.name, Is.EqualTo("Rival"));
            TestCapture.Capture("GhostResult2P.png");
        }

        [UnityTest]
        public IEnumerator ACategoryThatFailedLoadsAgainWhenEntered()
        {
            var row = fixture.AddReplay(chart, "Oni", Recording(), "Rival", 978000);
            fixture.FailReplays = 1;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect();
            yield return OpenList(false);
            var list = Object.FindAnyObjectByType<GhostSelectScene>();
            yield return Waits.Until(() => !list.IsLoading && fixture.Requests.Count(r => r == "GET /api/v1/replays") == 1, seconds: 10);
            yield return null;
            Assert.That(list.Rows, Is.Empty);
            Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/replays"), Is.EqualTo(1), "A failed page is not asked for again by itself.");
            // Entering the server shows its category again, which asks once more.
            list.Confirm();
            yield return Waits.Until(() => list.Rows.Count == 1, () => "Rows: " + list.Status, 10);
            Assert.That(list.Rows.Single().ScoreId, Is.EqualTo((string)row["id"]));
        }

        [UnityTest]
        public IEnumerator APlayOfReplacedFilesIsRefused()
        {
            var row = fixture.AddReplay(chart, "Oni", Recording(), "Rival", 978000);
            for (int i = 0; i < 11; i++) fixture.AddReplay(chart, "Oni", Recording(), "Other" + i, 900000 + i);
            // A row this version cannot play is skipped; the rest of its page is listed.
            fixture.AddReplay(chart, "Oni", Recording(), "Odd", 1)["course"] = "Tower";
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect();
            yield return OpenList(false);
            var list = Object.FindAnyObjectByType<GhostSelectScene>();
            yield return Waits.Until(() => list.Rows.Count == 12, () => "Rows: " + list.Status, 10);
            // もどる opens the column and comes again after every ten plays.
            var kinds = list.Entries(2).Select(e => e.Kind).ToList();
            Assert.That(kinds.Count, Is.EqualTo(14));
            Assert.That(Enumerable.Range(0, kinds.Count).Where(i => kinds[i] == GhostSelectScene.Kind.Back), Is.EqualTo(new[] { 0, 11 }));
            list.Confirm(); list.Confirm();
            Assert.That(list.SelectedRow.ScoreId, Is.EqualTo((string)row["id"]));
            for (int i = 0; i < 10; i++) list.Move(1);
            yield return null;
            Assert.That(list.Focused.Kind, Is.EqualTo(GhostSelectScene.Kind.Back));
            TestCapture.Capture("GhostSelectBackEveryTen.png");
            for (int i = 0; i < 10; i++) list.Move(-1);
            // The song's files were replaced after the list came.
            row["isLatest"] = false;
            list.Pick();
            yield return Waits.Until(() => !list.IsStarting, seconds: 10);
            Assert.That(list.Status, Does.Contain("changed"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.GhostSelectScene));
            list.Leave();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.EntryScene, seconds: 10);
        }
    }
}
