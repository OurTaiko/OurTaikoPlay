using System;
using System.Linq;
using OurTaiko.Multi.Proto;
using OurTaiko.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Multi
{
    // What a マルチ演奏 room asks of the game wherever the player is. It is made when the room screen
    // first opens and stays for the rest of the run:
    //  - when the room starts loading a round, it loads the room's song for this player from
    //    whichever screen is up (the room, song select, the last round's result);
    //  - it brings the player back from song select once their pick is in or picking is over;
    //  - over song select and the result screen it shows the room's number, what the room is doing
    //    and the time left, and anywhere it shows the room's notices for a few seconds.
    public sealed class MultiDirector : MonoBehaviour
    {
        public const int SortingOrder = 30500;
        public const float ToastSeconds = 3.5f;

        public static MultiDirector Instance { get; private set; }

        SceneSwitcher switcher;
        RectTransform stage;
        Image banner, toastPlate;
        TextMeshProUGUI bannerText, toastText;
        float toastUntil;
        // Song select has been opened for a pick since the room screen was entered from Entry.
        bool selectPrepared;

        // The banner's text and the toast now shown ("" when hidden), for tests.
        public string BannerText => banner != null && banner.gameObject.activeSelf ? bannerText.text : "";
        public string ToastText => toastPlate != null && toastPlate.gameObject.activeSelf ? toastText.text : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static MultiDirector Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject(nameof(MultiDirector));
            DontDestroyOnLoad(go);
            return Instance = go.AddComponent<MultiDirector>();
        }

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            Build();
            MultiSession.NoticeReceived += OnNotice;
            MultiSession.Closed += OnClosed;
        }

        void OnDestroy()
        {
            MultiSession.NoticeReceived -= OnNotice;
            MultiSession.Closed -= OnClosed;
            if (Instance == this) Instance = null;
        }

        void OnNotice(string code) => Toast(MultiText.Describe(code));

        // The room screen says why itself (MultiSession.ClosedCode); elsewhere it is said here.
        void OnClosed(string code)
        {
            if (code != "LEFT" && SceneSwitcher.CurrentScene != SceneSwitcher.MultiRoomScene) Toast(MultiText.Describe(code));
        }

        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            toastText.text = text;
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        // A new visit from Entry: song select starts from its top again.
        public void BeginVisit() => selectPrepared = false;

        // Whether a song can be the room's: one of the site's sources has it.
        public static bool CanShare(SongDefinition song)
        {
            var chart = OnlineManager.Instance != null ? OnlineManager.Instance.ChartOf(song) : null;
            return chart != null && MultiSession.SourceOf(chart.Server).Length > 0;
        }

        // Opens song select for the player's pick.
        public bool OpenSongSelect()
        {
            if (switcher.IsInputBlocked || !MultiSession.InRoom) return false;
            switcher.SeatLeadAlone();
            switcher.PracticeMode = false;
            switcher.MultiPick = true;
            if (!selectPrepared)
            {
                selectPrepared = true;
                SongSelectManager.EnsureInstance().Reset();
                LocalSongLibrary.EnsureInstance().Refresh();
            }
            switcher.SwitchScene(SceneSwitcher.SongSelectScene);
            return true;
        }

        // Song select's decision in a room: the song and the course go to the room.
        public static bool Pick(SongDefinition song, string course)
        {
            var chart = OnlineManager.Instance != null ? OnlineManager.Instance.ChartOf(song) : null;
            string source = chart != null ? MultiSession.SourceOf(chart.Server) : "";
            if (source.Length == 0 || !MultiSession.InRoom) return false;
            MultiSession.PickSong(source, chart.Id, course);
            return true;
        }

        void Update()
        {
            string scene = SceneSwitcher.CurrentScene;
            var room = MultiSession.Room;
            var me = MultiSession.Me;
            if (!switcher.IsSwitching)
            {
                if (scene != SceneSwitcher.SongSelectScene) switcher.MultiPick = false;
                // Out of song select once the pick is in, picking is over, or the room is gone.
                else if (switcher.MultiPick && (room == null || room.Phase != Phase.SongSelect || me == null || me.Picked))
                    switcher.SwitchScene(SceneSwitcher.MultiRoomScene);
            }
            if (room != null && room.Phase == Phase.Loading && MultiSession.LaunchedRound != room.Round) TryLaunch(room, me, scene);
            Show(room, scene);
        }

        // The room is loading a round this player is in: the room's song, on this player's course.
        void TryLaunch(Room room, Member me, string scene)
        {
            if (me == null || !me.Playing || me.Loaded || room.Song == null) return;
            // A play still on screen ends first; a switch under way or an open overlay is waited for.
            if (SceneSwitcher.IsPlayScene(scene) || scene == SceneSwitcher.SongLoadingScene || switcher.IsInputBlocked) return;
            MultiSession.LaunchedRound = room.Round;
            try
            {
                var song = SongOf(room.Song);
                var chart = OnlineManager.EnsureInstance().ChartOf(song);
                if (!chart.Blocks.Any(block => block.Course == me.Course)) throw new MultiException("COURSE_MISSING");
                if (!switcher.Play(PlayRequest.MultiRound(song, me.Course, room.Round))) throw new MultiException("LOAD_FAILED");
            }
            catch (Exception error)
            {
                Log.Warn("Multi", "Could not load the room's song: " + error.Message, this);
                string code = error is MultiException known ? known.Code : "LOAD_FAILED";
                MultiSession.Loaded(false, code);
                Toast(MultiText.Describe(code));
            }
        }

        // The room's song as one of the game's online songs. The room sends the chart as its source
        // describes it, so a song outside the lists the game loaded is known too.
        public static SongDefinition SongOf(Song roomSong)
        {
            var endpoint = MultiSession.EndpointOf(roomSong.Source) ?? throw new MultiException("SOURCE_UNKNOWN");
            var online = OnlineManager.EnsureInstance();
            FanmadeChart chart;
            if (roomSong.ChartJson.Length > 0) chart = FanmadeChart.From(Json.Parse(roomSong.ChartJson.ToStringUtf8()), endpoint.Id);
            else chart = online.Client?.Chart(endpoint.Id, roomSong.ChartId);
            if (chart == null || chart.Id != roomSong.ChartId || !chart.IsPlayable) throw new MultiException("CHART_NOT_FOUND");
            return online.SearchSong(chart) ?? throw new MultiException("CHART_NOT_FOUND");
        }

        // ---------------------------------------------------------------- banner and toast

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            stage = new GameObject("Stage", typeof(RectTransform)).GetComponent<RectTransform>();
            stage.SetParent(transform, false);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(1920, 1080);

            var art = MultiArt.Load();
            banner = SkinUi.Image("Banner", stage, art.panel, 980, 84);
            banner.rectTransform.Center(960, 52);
            bannerText = SkinUi.Text("Text", banner.transform, 36);
            Centre(bannerText.rectTransform);
            toastPlate = SkinUi.Image("Toast", stage, art.panel, 1100, 92);
            toastText = SkinUi.Text("Text", toastPlate.transform, 38);
            Centre(toastText.rectTransform);
            banner.gameObject.SetActive(false);
            toastPlate.gameObject.SetActive(false);
        }

        static void Centre(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        void Show(Room room, string scene)
        {
            bool overOtherScreens = room != null && (scene == SceneSwitcher.SongSelectScene && switcher.MultiPick || scene == SceneSwitcher.ResultScene);
            if (banner.gameObject.activeSelf != overOtherScreens) banner.gameObject.SetActive(overOtherScreens);
            if (overOtherScreens)
            {
                long left = MultiSession.RemainingMs;
                string text = MultiText.Format("banner.room", room.Code) + "　" + PhaseText(room);
                if (left >= 0) text += "　" + MultiText.Format("banner.left", (left + 999) / 1000);
                bannerText.text = text;
                bannerText.Squeeze(920);
            }
            bool toast = Time.unscaledTime < toastUntil;
            if (toastPlate.gameObject.activeSelf != toast) toastPlate.gameObject.SetActive(toast);
            if (toast)
            {
                // Clear of the lane and the scoreboard in a play, below the banner elsewhere.
                toastPlate.rectTransform.Center(960, SceneSwitcher.IsPlayScene(scene) ? 640 : 160);
                toastText.Squeeze(1040);
            }
        }

        // What the room is doing, as the banner and the room screen head it.
        public static string PhaseText(Room room)
        {
            switch (room.Phase)
            {
                case Phase.Lobby: return MultiText.Get("phase.lobby");
                case Phase.SongSelect: return MultiText.Get("phase.songSelect");
                case Phase.SongDraw: return MultiText.Get("phase.songDraw");
                case Phase.Course: return MultiText.Get("phase.course");
                case Phase.Loading: return MultiText.Get("phase.loading");
                case Phase.Playing: return MultiText.Get("phase.playing");
                case Phase.Result: return MultiText.Get("phase.result");
                default: return "";
            }
        }
    }
}
