using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace OurTaiko
{
    // How a switch covers the screen: the default dark fade, or the Nijiiro song-loading curtain.
    public enum TransitionStyle { Fade, Curtain }

    // Global UI control, created before the first scene and retained across every load.
    public sealed class SceneSwitcher : MonoBehaviour
    {
        public const string GameScene = "SinglePlayScene";
        public const string SongSelectScene = "SongSelect", ResultScene = "Result", EntryScene = "Entry";
        // The first scene and where Back falls back to.
        public const string MenuScene = EntryScene;
        public const string SongLoadingScene = "SongLoadingScene";
        public const string SettingScene = "GlobalSettingScene";
        public static SceneSwitcher Instance { get; private set; }
        public static Camera MainCamera { get; private set; }
        public static string CurrentScene { get; private set; } = "";
        public static string LastScene { get; private set; } = "";
        public static event EventHandler<(string NewScene, string OldScene)> OnSceneChanged;

        [SerializeField] CanvasGroup transition;
        [SerializeField] TMP_Text loadingText;
        [SerializeField] SongTransition songTransition;
        [SerializeField, Min(0.01f)] float closeDuration = 0.9f;
        [SerializeField, Min(0.01f)] float openDuration = 0.8f;

        public SongDefinition SelectedSong { get; private set; }
        // TJA COURSE value chosen on the song list; null plays the SongDefinition's own course.
        public string SelectedCourse { get; private set; }
        public bool AutoPlay { get; private set; }
        // Scene that started the current song; Back and the result screen return there.
        public string ReturnScene { get; private set; } = MenuScene;
        public PlayResult LastResult { get; private set; }
        // global_data.last_difficulty / songs_played for the single local player.
        public int LastDifficulty { get; set; } = -1;
        public int SongsPlayed { get; private set; }
        public bool IsSwitching { get; private set; }
        public bool IsCovered => IsCurtainClosed || (transition != null && transition.alpha >= 0.999f);
        public bool IsCurtainClosed => songTransition != null && songTransition.IsClosed;
        public SongTransition Curtain => songTransition;
        public bool IsInputBlocked => IsSwitching || IsCovered || (transitionTask != null && !transitionTask.IsCompleted);
        public event Action<string> SceneChanging;

        Task switchTask, transitionTask;
        CancellationTokenSource transitionCancellation;
        // Chart parsed by SongLoadingScene for the next SinglePlayScene, taken once.
        TaikoChart preparedChart;
        SongDefinition preparedSong;
        string preparedCourse;

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
            var existing = FindFirstObjectByType<SceneSwitcher>();
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
            SceneManager.activeSceneChanged -= OnUnitySceneChanged;
            SceneManager.activeSceneChanged += OnUnitySceneChanged;
            CurrentScene = SceneManager.GetActiveScene().name;
            MainCamera = Camera.main;
            SetTransitionState(false);
            if (songTransition != null) songTransition.Hide();
            QualitySettings.vSyncCount = 0;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            Application.targetFrameRate = 120;
        }

        void OnUnitySceneChanged(Scene previous, Scene next)
        {
            LastScene = CurrentScene;
            CurrentScene = next.name;
            MainCamera = Camera.main;
            EventSystem.current?.SetSelectedGameObject(null);
            OnSceneChanged?.Invoke(this, (CurrentScene, LastScene));
        }

        public void Play(SongDefinition song, bool autoPlay = false) => Play(song, null, autoPlay);
        public void Play(SongDefinition song, string course, bool autoPlay)
        {
            if (IsInputBlocked || song == null) return;
            SelectedSong = song; SelectedCourse = course; AutoPlay = autoPlay;
            preparedChart = null;
            if (CurrentScene != GameScene && CurrentScene != ResultScene && CurrentScene != SongLoadingScene && !string.IsNullOrEmpty(CurrentScene))
                ReturnScene = CurrentScene;
            // song_select.cpp select_song: the rainbow curtain closes, SongLoadingScene loads under it.
            if (songTransition == null || !Application.CanStreamedLevelBeLoaded(SongLoadingScene)) { SwitchScene(GameScene); return; }
            ShowSongOnCurtain(song);
            SwitchScene(SongLoadingScene, TransitionStyle.Curtain, false);
        }

        public void ShowSongOnCurtain(SongDefinition song)
        {
            if (songTransition == null || song == null) return;
            // The play scene reports an unreadable chart; the curtain just falls back to the asset name.
            try
            {
                var info = SongInfo.Read(song.chart.text);
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

        public void SetPreparedChart(SongDefinition song, string course, TaikoChart chart)
        {
            preparedSong = song; preparedCourse = course; preparedChart = chart;
        }

        public TaikoChart TakePreparedChart(SongDefinition song, string course)
        {
            var chart = preparedSong == song && preparedCourse == course ? preparedChart : null;
            preparedChart = null; preparedSong = null; preparedCourse = null;
            return chart;
        }
        public void Restart() => SwitchScene(GameScene);
        public void ReturnToMenu() => SwitchScene(Application.CanStreamedLevelBeLoaded(ReturnScene) ? ReturnScene : MenuScene);
        public void ShowResult(PlayResult result)
        {
            if (result == null || IsSwitching) return;
            LastResult = result;
            SongsPlayed++;
            SwitchScene(ResultScene);
        }

        public void SwitchScene(string sceneName, bool autoFadeOut = true) => SwitchScene(sceneName, TransitionStyle.Fade, autoFadeOut);
        public async void SwitchScene(string sceneName, TransitionStyle style, bool autoFadeOut = true)
        {
            try { await BeginSwitch(sceneName, Task.CompletedTask, autoFadeOut, style); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
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

        Task BeginSwitch(string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style)
        {
            if (IsSwitching) return switchTask;
            if (preparation == null) return Task.FromException(new ArgumentNullException(nameof(preparation)));
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
                return Task.FromException(new ArgumentException("Scene is not enabled in Build Settings: " + sceneName, nameof(sceneName)));
            IsSwitching = true;
            var completion = new TaskCompletionSource<bool>();
            switchTask = completion.Task;
            _ = CompleteSwitchAsync(completion, sceneName, preparation, autoFadeOut, style);
            return switchTask;
        }

        async Task CompleteSwitchAsync(TaskCompletionSource<bool> completion, string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style)
        {
            try { await SwitchSceneInternalAsync(sceneName, preparation, autoFadeOut, style); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        async Task SwitchSceneInternalAsync(string sceneName, Task preparation, bool autoFadeOut, TransitionStyle style)
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
                lifetime.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                if (operation == null) throw new InvalidOperationException("Could not load scene: " + sceneName);
                while (!operation.isDone) await Awaitable.NextFrameAsync(lifetime);
                // MajdataPlay waits for target initialization, then another 50 ms before opening.
                await Awaitable.NextFrameAsync(lifetime);
                float openAt = Time.realtimeSinceStartup + 0.05f;
                while (Time.realtimeSinceStartup < openAt) await Awaitable.NextFrameAsync(lifetime);
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
            catch (Exception error) { Debug.LogException(error, this); }
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
            float started = Time.realtimeSinceStartup;
            while (!Mathf.Approximately(from, to) && Time.realtimeSinceStartup - started < duration)
            {
                float progress = Mathf.Clamp01((Time.realtimeSinceStartup - started) / duration);
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
            transitionCancellation?.Cancel();
            transitionCancellation?.Dispose();
            Instance = null; MainCamera = null;
        }
    }
}
