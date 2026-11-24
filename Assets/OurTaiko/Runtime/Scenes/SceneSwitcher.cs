using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace OurTaiko
{
    // How a switch covers the screen: the default dark fade, the Nijiiro song-loading curtain, or
    // 段位道場's loading cover (DanLoadingCover).
    public enum TransitionStyle { Fade, Curtain, Dan }

    // One seat's part of a finished play, for the result screen: its result and the record saved and
    // uploaded with it (null: not kept, as a ghost's lane).
    public readonly struct SeatResult
    {
        public readonly PlayResult Result;
        public readonly Online.PlayRecord Record;

        public SeatResult(PlayResult result, Online.PlayRecord record)
        {
            Result = result;
            Record = record;
        }
    }

    // Global UI control, created before the first scene and retained across every load.
    public sealed partial class SceneSwitcher : MonoBehaviour
    {
        public const string GameScene = "Play.Single";
        public const string PracticeScene = "Play.Practice";
        // Both seats play here once 2P has joined (issue #5).
        public const string DoublePlayScene = "Play.Double";
        public bool PracticeMode { get; set; }
        // The sides with a player this credit: 1P, 2P (a player who joined alone with the 2P drum on
        // Entry) or both. Entry seats them as it is left, and 2P may join a lone 1P in song select. A
        // switcher that never passed Entry (a scene opened directly) has one player, on 1P.
        public IReadOnlyCollection<Side> Seated => seated;
        readonly HashSet<Side> seated = new HashSet<Side> { Side.P1 };
        public bool IsSeated(Side side) => seated.Contains(side);
        // The side that leads what there is one of: a lone player's own side, 1P when two play. Its
        // drum works the menus and every single-player mode, and its options, account and nameplate
        // are the lone player's. InputManager.Lead follows it.
        public Side Lead => seated.Contains(Side.P1) ? Side.P1 : Side.P2;

        // Seats these sides, and only these (1P alone when none is given).
        public void SetSeats(params Side[] sides)
        {
            seated.Clear();
            foreach (var side in sides) seated.Add(side);
            if (seated.Count == 0) seated.Add(Side.P1);
            InputManager.Lead = Lead;
        }

        // A side joins the players already seated.
        public void Seat(Side side)
        {
            seated.Add(side);
            InputManager.Lead = Lead;
        }

        // A single-player mode: the leading player stays, the other leaves (a lone 2P stays 2P).
        public void SeatLeadAlone() => SetSeats(Lead);
        public string SelectedPlayScene => PracticeMode ? PracticeScene : seated.Count == 2 || PlaysGhost ? DoublePlayScene : GameScene;
        // The request's mode, where the scene it is played in has it (practice has neither).
        public bool PlaysGhost => Request?.Ghost != null && !PracticeMode;
        public bool PlaysMulti => Request?.Multi != null && !PracticeMode;
        public static bool IsPlayScene(string scene) => scene == GameScene || scene == PracticeScene || scene == DoublePlayScene;
        public const string SongSelectScene = "CourseSelect.Enso", ResultScene = "Result.Enso", EntryScene = "Entry";
        // The first scene and where Back falls back to.
        public const string MenuScene = EntryScene;
        public const string SongLoadingScene = "SongLoadingScene";
        // The ゲーム設定 board's scene value in Entry; the board opens the settings overlay.
        public const string SettingScene = "GlobalSettingScene";
        // The first scene: the title card shown while the built-in servers connect.
        public const string PresentedScene = "Presented";
        // 段位道場: Entry's board opens the course select; a course is played in Play.Dan.
        public const string DanSelectScene = "CourseSelect.Dan", DanPlayScene = "Play.Dan";
        public const string GhostSelectScene = "CourseSelect.Ghost";
        // The course chosen on CourseSelect.Dan, for Play.Dan and the dan result; the ribbon returns to it.
        public DanCourseEntry SelectedDan { get; set; }
        // The highest rank among the listed courses (DanCourse.DanIndex): passing that one first
        // shows the result's 祝 popup.
        public int DanIndexMax { get; set; } = -1;
        // The course being played: SongLoadingScene fills its charts, Play.Dan plays it.
        public DanRun CurrentDan { get; private set; }
        bool danLoadPending;
        public static SceneSwitcher Instance { get; private set; }
        public static Camera MainCamera { get; private set; }
        public static string CurrentScene { get; private set; } = "";
        public static string LastScene { get; private set; } = "";
        public static event EventHandler<(string NewScene, string OldScene)> OnSceneChanged;
        // The scene the last switch left (CourseSelect.Dan plays its whole door intro only after Entry).
        public string FromScene { get; private set; } = "";

        [SerializeField] CanvasGroup transition;
        [SerializeField] TMP_Text loadingText;
        [SerializeField] SongTransition songTransition;
        [SerializeField] DanLoadingCover danCover;
        [SerializeField, Min(0.01f)] float closeDuration = 0.9f;
        [SerializeField, Min(0.01f)] float openDuration = 0.8f;

        // What the play scenes play (PlayRequest): set by Play, PlayDan and the play mode switch, or by
        // Select for a play scene opened directly. It stays for restarts; null before the first.
        public PlayRequest Request { get; private set; }
        // Scene that started the current song; Back and the result screen return there.
        public string ReturnScene { get; private set; } = MenuScene;
        // Each seat's result of the last play, in its board's place: 1P's first (a ghost battle: the
        // lane above first). Null for a seat the play did not have.
        public PlayResult ResultOf(int seat) => seat < lastResults.Length ? lastResults[seat] : null;
        PlayResult[] lastResults = Array.Empty<PlayResult>();
        // Consumed once by ResultScene; reopening a result must not save or upload it again.
        SongDefinition completedSong;
        Online.PlayRecord[] completedRecords = Array.Empty<Online.PlayRecord>();
        // global_data.last_difficulty for each seat (the course its cursor starts on); songs_played
        // for the credit.
        public int[] LastDifficulty { get; } = { -1, -1 };
        public int SongsPlayed { get; private set; }
        public bool IsSwitching { get; private set; }
        public bool IsCovered => IsCurtainClosed || (transition != null && transition.alpha >= 0.999f);
        public bool IsCurtainClosed => songTransition != null && songTransition.IsClosed || danCover != null && danCover.IsClosed;
        public SongTransition Curtain => songTransition;
        public DanLoadingCover DanCover => danCover;
        // The settings overlay, and any layer over the scene (ModalLayers), take the keys while up.
        public bool IsInputBlocked => IsSwitching || IsCovered || (transitionTask != null && !transitionTask.IsCompleted) || SettingsOverlay.BlocksInput
            || ModalLayers.BlocksInput;
        public event Action<string> SceneChanging;

        Task switchTask, transitionTask;
        CancellationTokenSource transitionCancellation;
        // Charts parsed by SongLoadingScene for the next play scene, one per seat, taken once.
        readonly TaikoChart[] preparedCharts = new TaikoChart[2];
        readonly SongDefinition[] preparedSongs = new SongDefinition[2];
        readonly string[] preparedCourses = new string[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null; MainCamera = null;
            CurrentScene = LastScene = "";
            OnSceneChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        public static SceneSwitcher EnsureInstance()
        {
            if (Instance != null) return Instance;
            // Also handles entering Play mode with scene/domain reload disabled.
            var existing = FindAnyObjectByType<SceneSwitcher>();
            if (existing != null) { existing.Initialize(); return Instance; }
            var prefab = Resources.Load<SceneSwitcher>(nameof(SceneSwitcher));
            if (prefab == null) throw new InvalidOperationException("The global Resources/SceneSwitcher prefab is missing.");
            Instantiate(prefab).name = nameof(SceneSwitcher);
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            if (transition == null || loadingText == null)
                throw new InvalidOperationException("SceneSwitcher requires its transition CanvasGroup and loading text.");
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InputManager.Lead = Lead;
            SceneManager.activeSceneChanged -= OnUnitySceneChanged;
            SceneManager.activeSceneChanged += OnUnitySceneChanged;
            CurrentScene = SceneManager.GetActiveScene().name;
            MainCamera = Camera.main;
            SetTransitionState(false);
            if (songTransition != null) songTransition.Hide();
            if (danCover != null) danCover.Hide();
            var settings = SettingManager.EnsureInstance();
            settings.Changed -= ApplyDisplay;
            settings.Changed += ApplyDisplay;
            ApplyDisplay(settings.Settings);
        }

        // VSync, frame interval and target frame rate come from Display settings (120 FPS default).
        static void ApplyDisplay(GameSettings settings) => settings.display.Apply();

        void OnUnitySceneChanged(Scene previous, Scene next)
        {
            LastScene = CurrentScene;
            CurrentScene = next.name;
            MainCamera = Camera.main;
            DisplayRefreshRate.Report();
            EventSystem.current?.SetSelectedGameObject(null);
            OnSceneChanged?.Invoke(this, (CurrentScene, LastScene));
        }

        // Plays the request: SongLoadingScene prepares it under the curtain, then the play scene opens.
        // A ghost battle and a room's round have one player, the leading one (a lone 2P stays 2P),
        // and are never practice. A room's round returns to the room, any other play to the scene it
        // was started from.
        public bool Play(PlayRequest request)
        {
            if (IsInputBlocked || request?.Song == null || request.Ghost is { Record: null }) return false;
            if (request.Ghost != null || request.Multi != null) { SeatLeadAlone(); PracticeMode = false; }
            if (request.Multi != null) MultiPick = false;
            Select(request);
            ResetPendingPlay();
            if (request.Multi != null) ReturnScene = MultiRoomScene;
            else if (!IsPlayScene(CurrentScene) && CurrentScene != ResultScene && CurrentScene != SongLoadingScene && !string.IsNullOrEmpty(CurrentScene))
                ReturnScene = CurrentScene;
            LoadSelectedPlay();
            return true;
        }

        // song_select.cpp select_song: the rainbow curtain closes, SongLoadingScene parses the chart and
        // decodes the audio under it, then opens the selected play scene.
        void LoadSelectedPlay()
        {
            if (songTransition == null || !Application.CanStreamedLevelBeLoaded(SongLoadingScene)) { SwitchScene(SelectedPlayScene); return; }
            ShowSongOnCurtain(Request.Song);
            SwitchScene(SongLoadingScene, TransitionStyle.Curtain, false);
        }

        // Nothing of an earlier play reaches the one about to load: no pending course load, no result
        // still to be saved, no chart prepared for another play.
        void ResetPendingPlay()
        {
            danLoadPending = false;
            completedSong = null; completedRecords = Array.Empty<Online.PlayRecord>();
            Array.Clear(preparedCharts, 0, preparedCharts.Length);
            Array.Clear(preparedSongs, 0, preparedSongs.Length);
            Array.Clear(preparedCourses, 0, preparedCourses.Length);
        }

        // 段位道場: the loading cover closes over the course select (with the course's plaque) and
        // SongLoadingScene prepares every song (downloading a server's first) before Play.Dan opens it.
        public bool PlayDan(DanCourseEntry entry)
        {
            if (IsInputBlocked || entry == null || !entry.IsPlayable) return false;
            CurrentDan?.Release();
            SelectedDan = entry;
            CurrentDan = new DanRun(entry);
            SeatLeadAlone(); PracticeMode = false;
            Select(new PlayRequest(entry.Songs[0].Song, entry.Songs[0].Course, PlayOptions.ForSeat(Lead.Index()).auto));
            ResetPendingPlay();
            ReturnScene = DanSelectScene;
            if (songTransition == null && danCover == null || !Application.CanStreamedLevelBeLoaded(SongLoadingScene)) { SwitchScene(DanPlayScene); return true; }
            danLoadPending = true;
            string language = SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "ja";
            if (danCover != null) danCover.SetCourse(entry.Course.Title(language), entry.Course.Color);
            else songTransition.SetSong(entry.Course.Title(language), "");
            SwitchScene(SongLoadingScene, danCover != null ? TransitionStyle.Dan : TransitionStyle.Curtain, false);
            return true;
        }

        // The last course played, for Result.Dan (P4); without that scene the course select follows.
        public const string DanResultScene = "Result.Dan";
        public DanPlayResult LastDan { get; private set; }

        public void ShowDanResult(DanPlayResult result)
        {
            if (result == null || IsSwitching) return;
            LastDan = result;
            SwitchScene(Application.CanStreamedLevelBeLoaded(DanResultScene) ? DanResultScene : DanSelectScene);
        }

        // SongLoadingScene asks once whether it is loading a course.
        public DanRun TakeDanLoad()
        {
            if (!danLoadPending) return null;
            danLoadPending = false;
            return CurrentDan;
        }

        // Makes `request` what the play scenes play next, replacing everything of the one before (its
        // mode, its folders' backdrops, practice's hand-over), without loading it. Each seat's side
        // is decided here, alone or two together as the seats stand now.
        public void Select(PlayRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            request.DecideSides(Lead, seated.Count == 2);
            practiceEntry = request.PracticeEntry;
        }

        public void ShowSongOnCurtain(SongDefinition song)
        {
            if (songTransition == null || song == null) return;
            // The play scene reports an unreadable chart; the curtain just falls back to the asset name.
            try
            {
                var info = song.ReadDisplayInfo();
                songTransition.SetSong(info.Title, info.Subtitle);
            }
            catch (Exception) { songTransition.SetSong(song.name, ""); }
        }

        // Shows the curtain parked on its title frame without the close (SongLoadingScene run directly).
        public void ParkCurtain()
        {
            if (songTransition == null || IsCovered) return;
            songTransition.Park();
        }

        // The same for a course: its loading cover, closed at once.
        public void ParkDanCover()
        {
            if (IsCovered) return;
            if (danCover != null) danCover.Park();
            else ParkCurtain();
        }

        // Download progress and errors on whichever cover is up.
        public void SetCoverStatus(string text, Color color)
        {
            if (danCover != null && danCover.IsVisible) danCover.SetStatus(text, color);
            else songTransition?.SetStatus(text, color);
        }

        public void SetPreparedChart(SongDefinition song, string course, TaikoChart chart, int seat = 0)
        {
            preparedSongs[seat] = song; preparedCourses[seat] = course; preparedCharts[seat] = chart;
        }

        public TaikoChart TakePreparedChart(SongDefinition song, string course, int seat = 0)
        {
            var chart = preparedSongs[seat] == song && preparedCourses[seat] == course ? preparedCharts[seat] : null;
            preparedCharts[seat] = null; preparedSongs[seat] = null; preparedCourses[seat] = null;
            return chart;
        }
        // The backdrop SongLoadingScene prepared for the song (its files are here to draw). It is
        // kept, not taken: a restart of the same song plays on the same backdrop.
        SongDefinition stagedSong;
        StageChoice stagedChoice;

        public void SetPreparedStage(SongDefinition song, StageChoice choice)
        {
            stagedSong = song; stagedChoice = choice;
        }

        public StageChoice? PreparedStage(SongDefinition song) => song != null && stagedSong == song ? stagedChoice : (StageChoice?)null;

        // The backdrop mode of the scene a play of the selected song is in.
        public StageMode SelectedStageMode => PracticeMode ? StageMode.Practice : SelectedPlayScene == DoublePlayScene ? StageMode.Double : StageMode.Single;

        public void Restart() => SwitchScene(SelectedPlayScene);
        public void ReturnToMenu() => SwitchScene(Application.CanStreamedLevelBeLoaded(ReturnScene) ? ReturnScene : MenuScene);
        // A result with nothing to save (a result screen opened on its own).
        public void ShowResult(PlayResult result) => ShowResult(null, new SeatResult(result, null));

        // The play of `song` has ended: each seat's result in its board's place, 1P's first.
        public void ShowResult(SongDefinition song, params SeatResult[] seats)
        {
            if (seats == null || seats.Length == 0 || seats[0].Result == null || IsSwitching) return;
            lastResults = Array.ConvertAll(seats, seat => seat.Result);
            completedSong = song; completedRecords = Array.ConvertAll(seats, seat => seat.Record);
            SongsPlayed++;
            SwitchScene(ResultScene);
        }

        // The records to keep with each seat's result (ResultOf), once.
        internal bool TakeCompletedPlay(out SongDefinition song, out IReadOnlyList<Online.PlayRecord> records)
        {
            song = completedSong; records = completedRecords;
            completedSong = null; completedRecords = Array.Empty<Online.PlayRecord>();
            return song != null;
        }

        public void SwitchScene(string sceneName, bool autoFadeOut = true) => SwitchScene(sceneName, TransitionStyle.Fade, autoFadeOut);
        public async void SwitchScene(string sceneName, TransitionStyle style, bool autoFadeOut = true)
        {
            try { await BeginSwitch(sceneName, Task.CompletedTask, autoFadeOut, style); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Log.Exception("Scene", error, this); }
        }

        public Task SwitchSceneAsync(string sceneName, bool autoFadeOut = true)
            => BeginSwitch(sceneName, Task.CompletedTask, autoFadeOut, TransitionStyle.Fade);

        public Task SwitchSceneAfterTaskAsync(string sceneName, Task taskToRun, bool autoFadeOut = true)
            => BeginSwitch(sceneName, taskToRun, autoFadeOut, TransitionStyle.Fade);

        public async Task<T> SwitchSceneAfterTaskAsync<T>(string sceneName, Task<T> taskToRun, bool autoFadeOut = true)
        {
            if (IsSwitching) throw new InvalidOperationException("A scene switch is already in progress.");
            await BeginSwitch(sceneName, taskToRun, autoFadeOut, TransitionStyle.Fade);
            return await taskToRun;
        }

        public Task SwitchSceneAfterFadeAsync(string sceneName, Func<Task> prepare)
            => BeginSwitch(sceneName, Task.CompletedTask, true, TransitionStyle.Fade, prepare);

        Task BeginSwitch(string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style, Func<Task> afterFade = null)
        {
            if (IsSwitching) return switchTask;
            if (preparation == null) return Task.FromException(new ArgumentNullException(nameof(preparation)));
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
                return Task.FromException(new ArgumentException("Scene is not enabled in Build Settings: " + sceneName, nameof(sceneName)));
            IsSwitching = true;
            var completion = new TaskCompletionSource<bool>();
            switchTask = completion.Task;
            _ = CompleteSwitchAsync(completion, sceneName, preparation, autoFadeOut, style, afterFade);
            return switchTask;
        }

        async Task CompleteSwitchAsync(TaskCompletionSource<bool> completion, string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style, Func<Task> afterFade)
        {
            try { await SwitchSceneInternalAsync(sceneName, preparation, autoFadeOut, style, afterFade); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        async Task SwitchSceneInternalAsync(string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style, Func<Task> afterFade)
        {
            var lifetime = destroyCancellationToken;
            try
            {
                EventSystem.current?.SetSelectedGameObject(null);
                SceneChanging?.Invoke(sceneName);
                SetLoadingText("");
                await StartTransitionAsync(true, style);
                while (!preparation.IsCompleted) await Awaitable.NextFrameAsync(lifetime);
                await preparation;
                if (afterFade != null) await afterFade();
                lifetime.ThrowIfCancellationRequested();
                FromScene = CurrentScene;
                Log.Info("Scene", FromScene + " → " + sceneName);
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                if (operation == null) throw new InvalidOperationException("Could not load scene: " + sceneName);
                while (!operation.isDone) await Awaitable.NextFrameAsync(lifetime);
                // MajdataPlay waits for target initialization, then another 50 ms before opening.
                await Awaitable.NextFrameAsync(lifetime);
                double openAt = GameTimeline.FrameTime + 0.05f;
                while (GameTimeline.FrameTime < openAt) await Awaitable.NextFrameAsync(lifetime);
                if (autoFadeOut) await StartTransitionAsync(false);
            }
            catch
            {
                // A failed/cancelled preparation must not strand the old scene behind the cover.
                if (!lifetime.IsCancellationRequested) await StartTransitionAsync(false);
                throw;
            }
            finally { IsSwitching = false; }
        }

        // Naming matches MajdataPlay: FadeIn closes the cover; FadeOut reveals the scene.
        public async void FadeIn() => await ObserveTransitionAsync(FadeInAsync());
        public async void FadeOut() => await ObserveTransitionAsync(FadeOutAsync());
        public async Task FadeInAsync()
        {
            if (IsSwitching) await switchTask;
            SetLoadingText("");
            await StartTransitionAsync(true, TransitionStyle.Fade);
        }
        public async Task FadeOutAsync()
        {
            if (IsSwitching) await switchTask;
            await StartTransitionAsync(false);
        }
        async Task ObserveTransitionAsync(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Log.Exception("Scene", error, this); }
        }

        public void SetLoadingText(string text) => SetLoadingText(text, Color.white);
        public void SetLoadingText(string text, Color color)
        {
            loadingText.text = text;
            loadingText.color = color;
        }

        // A cover keeps its style until it opens: a parked curtain stays closed for the next
        // switch and is the one that opens over the new scene.
        Task StartTransitionAsync(bool closing, TransitionStyle style = TransitionStyle.Fade)
        {
            if (closing && IsCurtainClosed) return transitionTask = Task.CompletedTask;
            transitionCancellation?.Cancel();
            transitionCancellation?.Dispose();
            transitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            bool dan = danCover != null && (closing ? style == TransitionStyle.Dan : danCover.IsVisible);
            if (dan) return transitionTask = danCover.PlayAsync(closing, transitionCancellation.Token);
            bool curtain = songTransition != null && (closing ? style == TransitionStyle.Curtain : songTransition.IsVisible);
            // The curtain's own full-screen raycast target blocks clicks while it is shown.
            return transitionTask = curtain ? songTransition.PlayAsync(closing, transitionCancellation.Token)
                : AnimateTransitionAsync(closing, transitionCancellation.Token);
        }

        async Task AnimateTransitionAsync(bool closing, CancellationToken cancellation)
        {
            transition.gameObject.SetActive(true);
            transition.blocksRaycasts = true;
            float from = transition.alpha, to = closing ? 1 : 0;
            float duration = closing ? closeDuration : openDuration;
            double started = GameTimeline.FrameTime;
            while (!Mathf.Approximately(from, to) && GameTimeline.FrameTime - started < duration)
            {
                float progress = Mathf.Clamp01((float)((GameTimeline.FrameTime - started) / duration));
                // The same OutQuint curve and real-time durations as MajdataPlay, in pure C#.
                transition.alpha = Mathf.Lerp(from, to, 1 - Mathf.Pow(1 - progress, 5));
                await Awaitable.NextFrameAsync(cancellation);
            }
            SetTransitionState(closing);
        }

        void SetTransitionState(bool closed)
        {
            transition.alpha = closed ? 1 : 0;
            transition.interactable = false;
            transition.blocksRaycasts = closed;
            transition.gameObject.SetActive(closed);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.activeSceneChanged -= OnUnitySceneChanged;
            if (SettingManager.Instance != null) SettingManager.Instance.Changed -= ApplyDisplay;
            transitionCancellation?.Cancel();
            transitionCancellation?.Dispose();
            Instance = null; MainCamera = null;
            InputManager.Lead = Side.P1;
        }
    }
}
