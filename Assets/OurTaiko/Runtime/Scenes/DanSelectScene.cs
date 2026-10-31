using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場 course select, reached from Entry's board (YataiDON DanSelectScreen with Nijiiro's
    // dan_select art and PyTaikoGreen's dan_select.lua layout). The courses are read here: each song
    // library root's Dan/*.otdan.json, then the DanServerTest/ stand-in for server courses. The
    // ribbon of course chips runs along the top, the selected course's songs and exams sit on the
    // yellow board, and a don opens the confirmation (オプション / はい / いいえ). Courses that cannot
    // be played (a song not found, a broken file) are listed greyed out with the reason. The 段位道場
    // doors (DanIntroView) open over it first, the whole intro after Entry, and the screen takes input
    // once they have. No timer (user decision: the simulator does not limit the player's time).
    // Three pages (DanSelectPages): ローカル, Fanmade and Database, the online ones listing only the
    // official courses and ending with the 外伝 entry, where a course's code is typed to challenge
    // it (GET /api/v1/game/dans/{code}); the course then joins the page.
    public sealed class DanSelectScene : MonoBehaviour
    {
        // The ribbon: chips every RibbonSpacing px around the centre (Nijiiro dan_ribbon x 960).
        public const float RibbonCenter = 960, RibbonSpacing = 96, ChipTop = 16;
        // Yellow board (animations 13–18): left/right/width over 116.67 ms, then top/height over 133.33 ms.
        const double BoxOutMs = 116.67, BoxUpMs = 133.33, ContentsDelayMs = 116.67 + 16.67 + 133.33, ContentsMs = 116.67;
        const double ConfirmDelayMs = 183.33, ConfirmFadeMs = 183.33;
        static readonly int[] GenreBannerOrder = { 0, 0, 1, 2, 3, 4, 5, 6, 7 };

        public DanSelectView view;
        public OptionPanelArt optionArt;
        public DanIntroView intro;

        [Header("Audio")]
        public AudioSource bgm;
        public AudioSource sfx, voice;
        public AudioClip don, ka, cancel, confirmBox, confirmVoice, enterVoice, optionsVoice;
        [Tooltip("song_select/dan_intro: the stamps of the whole door intro.")]
        public AudioClip introSound;

        public DanSelectFlow Flow { get; private set; }
        // The current page's courses.
        public IReadOnlyList<DanCourseEntry> Courses => courses;
        public IReadOnlyList<DanSelectPage> Pages => pages;
        public int Page { get; private set; }
        public bool IsEnteringCode => codeEntry != null && codeEntry.root.gameObject.activeSelf;
        public string CodeMessage => codeEntry?.message.text ?? "";
        public bool HasLeft { get; private set; }
        public OptionMenu Options { get; private set; }
        // The doors play once the cover has opened; the screen takes input after them.
        public bool FullIntro { get; private set; }
        public bool IntroDone => intro == null || !intro.IsPlaying && !double.IsNaN(introStartedAt);

        List<DanCourseEntry> courses = new List<DanCourseEntry>();
        readonly List<DanSelectPage> pages = new List<DanSelectPage>();
        readonly Dictionary<SongDefinition, SongInfo> displayInfo = new Dictionary<SongDefinition, SongInfo>();
        SceneSwitcher switcher;
        OptionPanel optionPanel;
        DanCourseResolver resolver;
        PageTabs tabs;
        CodeEntry codeEntry;
        ExamBestLines bestLines;
        // The code lookup under way, and the frame the input field consumed a key on.
        Task<string> lookup;
        string lookupCode;
        int fieldKeyFrame = -1;
        double ribbonFrom, ribbonTo, ribbonMovedAt = double.NegativeInfinity, boardShownAt, introStartedAt = double.NaN;
        int shownCourse = -1;
        string language = "ja";

        public double Now => GameTimeline.FrameTime * 1000;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            // 段位道場 is single-player: Entry keeps 2P off its board, and the course plays one seat.
            switcher.TwoPlayers = false;
            language = SettingManager.EnsureInstance().Settings.general.Language;
            Load();
            var all = pages.SelectMany(p => p.Courses).Where(c => !c.IsGaiden).ToList();
            switcher.DanIndexMax = all.Count > 0 ? all.Max(c => c.Course.DanIndex) : -1;
            // Back from a course: its page and chip again.
            int startPage = switcher.SelectedDan != null ? pages.FindIndex(p => p.Courses.Any(c => Same(c, switcher.SelectedDan))) : -1;
            OpenPage(Math.Max(0, startPage), startPage < 0 ? 0 : pages[startPage].Courses.FindIndex(c => Same(c, switcher.SelectedDan)));
            Bind();
            switcher.SceneChanging += OnSceneChanging;
            FullIntro = switcher.FromScene == SceneSwitcher.EntryScene;
            if (intro != null) intro.Show(DanIntroView.Frame(FullIntro, 0));
            Show(Now);
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, ka, cancel, confirmBox, introSound);
            voice.PrepareAudioTracks(confirmVoice, enterVoice, optionsVoice);
            if (bgm.clip != null) { bgm.loop = true; bgm.PlayAudio(); }
            Play(voice, enterVoice);
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            bgm.StopAudio(); voice.StopAudio();
        }

        static bool Same(DanCourseEntry a, DanCourseEntry b) => a.Course.Id == b.Course.Id && a.Course.Server == b.Course.Server && a.Path == b.Path;

        // The local courses (each library root in order) on the first page, each connected server's
        // official courses on its page, the server stand-in's on the page of its server.
        void Load()
        {
            var library = LocalSongLibrary.EnsureInstance();
            var local = library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).Distinct().ToList();
            var online = Online.OnlineManager.EnsureInstance();
            resolver = new DanCourseResolver(local, (server, id) => FindServerSong(online, server, id));
            var servers = new List<DanCourseEntry>();
            if (online.Client != null)
                foreach (var (server, dans) in online.Client.Dans) servers.AddRange(DanCourseLoader.LoadServer(server, dans, resolver));
            var standIns = DanCourseLoader.LoadServerTest(Path.Combine(GamePaths.Root, DanCourseLoader.ServerTestFolder), resolver);
            pages.Clear();
            pages.AddRange(DanSelectPages.Build(DanCourseLoader.LoadLocal(library.Roots, resolver), servers, standIns,
                Online.ServerList.BuiltIn[0].Url, Online.ServerList.BuiltIn[1].Url));
            foreach (var entry in pages.SelectMany(p => p.Courses).Concat(servers).Distinct())
            {
                if (entry.Error != null) Log.Warn("Dan", $"Dan course {entry.Path}: {entry.Error}");
                foreach (string warning in entry.Course.Warnings) Log.Warn("Dan", $"Dan course {entry.Path}: {warning}");
            }
        }

        // Shows a page with its chip at `selected` under the cursor.
        void OpenPage(int index, int selected)
        {
            Page = Math.Max(0, Math.Min(pages.Count - 1, index));
            courses = pages[Page].Courses;
            Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, Math.Max(0, selected));
            ribbonFrom = ribbonTo = Flow.Selected; ribbonMovedAt = double.NegativeInfinity;
            shownCourse = -1;
            tabs?.Show(Page);
        }

        // The page to the left (-1) or right (+1); the pages do not wrap.
        public bool SwitchPage(int delta)
        {
            if (switcher.IsInputBlocked || HasLeft || !IntroDone || IsEnteringCode || Flow.State != DanSelectFlow.Phase.Browsing) return false;
            int next = Page + delta;
            if (next < 0 || next >= pages.Count || next == Page) return false;
            Play(sfx, ka, true);
            OpenPage(next, 0);
            return true;
        }

        // A server's song by id: the endpoint whose base URL the course names, then its song.
        static SongDefinition FindServerSong(Online.OnlineManager online, string server, string id)
        {
            var endpoints = online.Client?.Endpoints;
            if (endpoints == null) return null;
            var ids = new HashSet<string>(endpoints.Where(e => string.Equals(e.Config.baseUrl, server, StringComparison.OrdinalIgnoreCase)).Select(e => e.Id));
            return online.Songs.FirstOrDefault(s => s.onlineChart != null && ids.Contains(s.onlineChart.Server) && s.onlineChart.Id == id);
        }

        void Bind()
        {
            for (int i = 0; i < view.chips.Length; i++)
            {
                var chip = view.chips[i];
                chip.hit.Clicked = () => TapChip(chip);
            }
            view.ribbonSwipe.Swiped = d => Ka(d);
            view.shade.Clicked = () => { if (Flow.State == DanSelectFlow.Phase.Confirm) Back(); };
            view.yesHit.Clicked = () => TapChoice(DanSelectFlow.Choice.Yes);
            view.noHit.Clicked = () => TapChoice(DanSelectFlow.Choice.No);
            view.optionHit.Clicked = () => TapChoice(DanSelectFlow.Choice.Option);
            optionPanel = new OptionPanel(view.options, optionArt);
            optionPanel.RowTapped += TapOptionRow;
            optionPanel.OutsideTapped += CloseOptions;
            view.confirm.gameObject.SetActive(false);
            var canvas = view.board.parent as RectTransform;
            // The chips name the rank in two big characters, stacked tightly as the arcade does.
            foreach (var chip in view.chips) DanPlateText.StyleChip(chip.name);
            // The plate reads top to bottom; the old horizontal line and the record under it are gone
            // (the arcade shows the bests under each exam instead).
            DanPlateText.StylePlate(view.horiName, DanPlateText.SelectPlateCenter);
            if (view.record != null) view.record.gameObject.SetActive(false);
            bestLines = new ExamBestLines(view);
            tabs = new PageTabs(canvas, view.empty, pages.Select(p => p.Title).ToList(), i => { if (i != Page) SwitchPage(i - Page); });
            tabs.Show(Page);
            codeEntry = new CodeEntry(canvas, view.empty, SubmitCode, CloseCodeEntry, () => fieldKeyFrame = Time.frameCount);
        }

        void Update()
        {
            double now = Now;
            ShowIntro(now);
            if (!switcher.IsInputBlocked && !HasLeft && IntroDone) HandleInput(now);
            Show(now);
        }

        void ShowIntro(double now)
        {
            if (intro == null || !intro.IsPlaying) return;
            if (double.IsNaN(introStartedAt))
            {
                if (switcher.IsInputBlocked) return;
                introStartedAt = now;
                if (FullIntro) Play(sfx, introSound, true);
            }
            intro.Show(DanIntroView.Frame(FullIntro, now - introStartedAt));
        }

        void HandleInput(double now)
        {
            if (IsEnteringCode)
            {
                // Typed letters also reach the drum keys; while the code is typed only Esc acts
                // here (Enter arrives as the field's onSubmit), and the field eats the first Esc.
                if (codeEntry.field.isFocused) return;
                if (Time.frameCount == fieldKeyFrame) return;
                if (Pressed(InputKey.Back)) CloseCodeEntry();
                else if (Pressed(InputKey.LeftDon, InputKey.RightDon, InputKey.Confirm)) SubmitCode(codeEntry.field.text);
                return;
            }
            if (Flow.State == DanSelectFlow.Phase.Options)
            {
                if (Options == null || Options.IsConfirmed) return;
                if (Pressed(InputKey.Back)) { CloseOptions(); return; }
                if (Pressed(InputKey.LeftKa, InputKey.MenuLeft)) ChangeOption(-1);
                else if (Pressed(InputKey.RightKa, InputKey.MenuRight)) ChangeOption(+1);
                else if (Pressed(InputKey.MenuUp)) { if (Options.Move(-1)) Play(sfx, ka, true); }
                else if (Pressed(InputKey.MenuDown)) { if (Options.Move(+1)) Play(sfx, ka, true); }
                else if (Pressed(InputKey.LeftDon, InputKey.RightDon, InputKey.Confirm)) { Play(sfx, don, true); Options.Confirm(); }
                return;
            }
            if (Pressed(InputKey.Back)) { Back(); return; }
            if (Pressed(InputKey.LeftDon, InputKey.RightDon, InputKey.Confirm)) Don();
            else if (Pressed(InputKey.LeftKa, InputKey.MenuLeft)) Ka(-1);
            else if (Pressed(InputKey.RightKa, InputKey.MenuRight)) Ka(1);
            else if (Pressed(InputKey.MenuUp)) SwitchPage(-1);
            else if (Pressed(InputKey.MenuDown)) SwitchPage(1);
        }

        static bool Pressed(params InputKey[] keys) => keys.Any(InputManager.GetKeyDown);

        // A drum face hit: the confirmation opens on a playable course, or decides there.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft || !IntroDone) return;
            double now = Now;
            if (Flow.State == DanSelectFlow.Phase.Browsing)
            {
                if (courses.Count > 0 && courses[Flow.Selected].IsGaiden) { Play(sfx, don, true); OpenCodeEntry(); return; }
                if (!Flow.Open(now)) { Play(sfx, cancel, true); return; }
                Play(sfx, don, true);
                Play(sfx, confirmBox, true);
                Play(voice, confirmVoice);
                return;
            }
            var choice = Flow.Decide(now);
            if (choice == null) return;
            switch (choice.Value)
            {
                case DanSelectFlow.Choice.Option:
                    Play(sfx, don, true);
                    Options = new OptionMenu(PlayOptions.ForSeat(0), optionArt.hitSounds != null ? optionArt.hitSounds.Count : 0);
                    optionPanel.Open(Options, now);
                    Play(voice, optionsVoice);
                    break;
                case DanSelectFlow.Choice.Yes:
                    Play(sfx, don, true);
                    Begin(courses[Flow.Selected]);
                    break;
                default:
                    Play(sfx, cancel, true);
                    break;
            }
        }

        // A rim hit: the ribbon moves (wrapping), or the confirmation's cursor does.
        public void Ka(int delta)
        {
            if (switcher.IsInputBlocked || HasLeft || !IntroDone) return;
            double now = Now;
            if (Flow.State == DanSelectFlow.Phase.Browsing)
            {
                double at = Ribbon(now);
                if (!Flow.Move(delta, now)) return;
                Play(sfx, ka, true);
                ribbonFrom = at; ribbonTo += delta; ribbonMovedAt = now;
            }
            else if (Flow.MoveChoice(delta, now)) Play(sfx, ka, true);
        }

        public void Back()
        {
            if (switcher.IsInputBlocked || HasLeft || !IntroDone) return;
            if (IsEnteringCode) { CloseCodeEntry(); return; }
            if (!Flow.Back()) return;
            Play(sfx, cancel, true);
            if (Flow.State != DanSelectFlow.Phase.Left) return;
            HasLeft = true;
            switcher.SwitchScene(SceneSwitcher.EntryScene);
        }

        void TapChip(DanSelectView.ChipView chip)
        {
            int index = Array.IndexOf(view.chips, chip);
            if (index < 0 || index >= chipCourse.Length || chipCourse[index] < 0 || Flow.State != DanSelectFlow.Phase.Browsing) return;
            int course = chipCourse[index];
            if (course == Flow.Selected) { Don(); return; }
            int delta = course - Flow.Selected, n = courses.Count;
            if (delta > n / 2) delta -= n; else if (delta < -n / 2) delta += n;
            Ka(delta);
        }

        void TapChoice(DanSelectFlow.Choice choice)
        {
            double now = Now;
            if (Flow.Cursor == choice) Don();
            else if (Flow.Point(choice, now)) Play(sfx, ka, true);
        }

        void ChangeOption(int direction)
        {
            if (!(direction < 0 ? Options.Left() : Options.Right())) return;
            Play(sfx, ka, true);
            optionPanel.Changed(direction, Now);
        }

        void TapOptionRow(int row, int direction)
        {
            if (Options == null || Options.IsConfirmed) return;
            if (direction == 0)
            {
                if (Options.Index == row) { Play(sfx, don, true); Options.Confirm(); }
                else { Play(sfx, ka, true); Options.Select(row); }
                return;
            }
            Options.Select(row);
            ChangeOption(direction);
        }

        void CloseOptions()
        {
            if (Options == null || Options.IsConfirmed) return;
            Play(sfx, don, true);
            Options.ConfirmAll();
        }

        void Begin(DanCourseEntry entry)
        {
            switcher.SelectedDan = entry;
            PlayOptions.ForSeat(0).Save();
            if (!Application.CanStreamedLevelBeLoaded(SceneSwitcher.DanPlayScene))
            {
                Log.Warn("Dan", "DanPlayScene is not in the build.");
                Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, Flow.Selected);
                return;
            }
            if (switcher.PlayDan(entry)) HasLeft = true;
            else Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, Flow.Selected);
        }

        // ---------------------------------------------------------------- 外伝: a course by its code

        void OpenCodeEntry()
        {
            codeEntry.Open(pages[Page].Title);
        }

        void CloseCodeEntry()
        {
            if (!IsEnteringCode) return;
            Play(sfx, cancel, true);
            codeEntry.Close();
            fieldKeyFrame = Time.frameCount;
        }

        // Asks the page's server for the course; the answer is taken in Update (PollLookup).
        public void SubmitCode(string typed)
        {
            if (!IsEnteringCode || lookup != null) return;
            string code = DanSelectPages.CleanCode(typed);
            if (code == null) { codeEntry.Say("英数字 6 文字のコードを入力してください"); return; }
            string server = pages[Page].Server;
            var endpoint = Online.OnlineManager.EnsureInstance().Client?.Endpoints
                .FirstOrDefault(e => string.Equals(e.Config.baseUrl.TrimEnd('/'), server, StringComparison.OrdinalIgnoreCase) && e.IsConnected);
            if (endpoint == null) { codeEntry.Say("サーバーに接続していません"); return; }
            Play(sfx, don, true);
            codeEntry.Say("検索中…");
            codeEntry.field.interactable = false;
            lookupCode = code;
            lookup = endpoint.RequestAsync("/api/v1/game/dans/" + code);
        }

        void PollLookup()
        {
            if (lookup == null || !lookup.IsCompleted) return;
            var task = lookup;
            lookup = null;
            if (codeEntry != null) codeEntry.field.interactable = true;
            if (!IsEnteringCode) return;
            if (task.IsFaulted || task.IsCanceled)
            {
                var error = task.Exception?.InnerExceptions.FirstOrDefault();
                codeEntry.Say(error is Online.HttpStatusException { Status: 404 } ? "コード " + lookupCode + " の段位はありません" : "取得できませんでした: " + (error?.Message ?? "?"));
                return;
            }
            DanCourseEntry entry;
            try { entry = resolver.Resolve(DanCourse.From(Online.Json.Parse(task.Result), pages[Page].Server), pages[Page].Server + "/api/v1/game/dans/" + lookupCode); }
            catch (Exception error) when (error is FormatException || error is Online.FanmadeException)
            {
                codeEntry.Say("取得できませんでした: " + error.Message);
                return;
            }
            int index = DanSelectPages.Insert(pages[Page], entry);
            codeEntry.Close();
            fieldKeyFrame = Time.frameCount;
            Play(sfx, confirmBox, true);
            OpenPage(Page, index);
        }

        static void Play(AudioSource source, AudioClip clip, bool oneShot = false)
        {
            if (source == null || clip == null) return;
            if (oneShot) { source.PlayAudioOneShot(clip); return; }
            source.StopAudio();
            source.clip = clip;
            source.PlayAudio();
        }

        // ---------------------------------------------------------------- drawing

        int[] chipCourse = Array.Empty<int>();

        // The ribbon's centre as a course index (fractional while sliding).
        double Ribbon(double now)
        {
            double t = Math.Min(1, Math.Max(0, (now - ribbonMovedAt) / DanSelectFlow.SlideMs));
            return ribbonFrom + (ribbonTo - ribbonFrom) * t;
        }

        void Show(double now)
        {
            PollLookup();
            if (optionPanel.Draw(now)) { Options = null; PlayOptions.ForSeat(0).Save(); Flow.OptionsClosed(); }
            view.empty.gameObject.SetActive(courses.Count == 0);
            view.board.gameObject.SetActive(courses.Count > 0);
            ShowRibbon(now);
            if (courses.Count == 0) { view.confirm.gameObject.SetActive(false); return; }
            if (shownCourse != Flow.Selected) { shownCourse = Flow.Selected; boardShownAt = now; ShowCourse(courses[shownCourse]); }
            ShowBoard(now - boardShownAt);
            ShowConfirm(now);
        }

        void ShowRibbon(double now)
        {
            if (chipCourse.Length != view.chips.Length) chipCourse = new int[view.chips.Length];
            int n = courses.Count, used = 0;
            double center = Ribbon(now);
            for (int i = 0; i < n && used < view.chips.Length; i++)
            {
                double d = i - center;
                d -= Math.Round(d / n) * n;
                float x = RibbonCenter + (float)d * RibbonSpacing;
                if (x < -RibbonSpacing || x > 1920 + RibbonSpacing) continue;
                var chip = view.chips[used];
                chipCourse[used++] = i;
                chip.root.gameObject.SetActive(true);
                chip.root.anchoredPosition = new Vector2(x, 0);
                var entry = courses[i];
                chip.folder.sprite = view.folders[Mathf.Clamp(entry.Course.Color, 0, view.folders.Length - 1)];
                chip.folder.color = entry.IsPlayable ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1);
                string name = Vertical(ChipLabel(entry));
                if (chip.name.text != name) chip.name.text = name;
            }
            for (int i = used; i < view.chips.Length; i++) { view.chips[i].root.gameObject.SetActive(false); chipCourse[i] = -1; }

            bool any = n > 0;
            view.cursor.gameObject.SetActive(any);
            view.arrowLeft.gameObject.SetActive(any);
            view.arrowRight.gameObject.SetActive(any);
            if (!any) return;
            float cx = RibbonCenter + (float)((Flow.Selected - center) - Math.Round((Flow.Selected - center) / n) * n) * RibbonSpacing;
            view.cursor.rectTransform.anchoredPosition = new Vector2(cx, view.cursor.rectTransform.anchoredPosition.y);
            view.cursor.Alpha(CursorAlpha(now));
            // box/arrow_r, arrow_l (80×96, centred 55 px either side). The arrows drift 10 px outward over a second after a move and fade out over its second half.
            double af = double.IsInfinity(ribbonMovedAt) ? 60 : (now - ribbonMovedAt) * 0.06;
            float drift = (float)(Math.Min(60, af) / 60 * 10);
            float aa = af > 60 ? 0 : af <= 30 ? 1 : (float)(1 - (af - 30) / 30);
            view.arrowRight.rectTransform.anchoredPosition = new Vector2(cx + 55 + drift, view.arrowRight.rectTransform.anchoredPosition.y);
            view.arrowLeft.rectTransform.anchoredPosition = new Vector2(cx - 55 - drift, view.arrowLeft.rectTransform.anchoredPosition.y);
            view.arrowRight.Alpha(aa);
            view.arrowLeft.Alpha(aa);
        }

        // DanNavigator dan_cursor_alpha: the cursor's breathing loop, one arcade second.
        static readonly float[] CursorTable = { 1.000f, 0.977f, 0.910f, 0.801f, 0.645f, 0.441f, 0.301f, 0.520f, 0.699f, 0.836f, 0.934f, 0.988f };

        public static float CursorAlpha(double ms)
        {
            double f = (ms * 0.06) % 60;
            if (f < 0) f += 60;
            double t = f / 5;
            int i = (int)t;
            float u = (float)(t - i);
            return CursorTable[i % 12] * (1 - u) + CursorTable[(i + 1) % 12] * u;
        }

        // A chip's name is written top to bottom, one character per line.
        static string Vertical(string text) => string.Join("\n", (text ?? "").Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()));

        // What a chip says: 外伝, the rank a server's course awards (五級 .. 達人, two characters), else
        // the course's own name (a local or unranked course). The plate carries the full name.
        public string ChipLabel(DanCourseEntry entry)
        {
            if (entry.IsGaiden) return DanSelectPages.GaidenTitle;
            int rank = entry.Course.DanIndex;
            return rank >= 0 ? DanResultFlow.RankName(rank) : entry.Course.Title(language);
        }

        // The yellow board opens like the song wheel's (animations 13–18); its contents fade in last.
        void ShowBoard(double t)
        {
            double p = Math.Min(1, Math.Max(0, t / BoxOutMs));
            double q = Math.Min(1, Math.Max(0, (t - BoxOutMs) / BoxUpMs));
            float left = (float)(960 - 630 * p), right = (float)(960 + 834 * p), width = (float)(1384 * p);
            float top = (float)(540 - 358 * q), height = (float)(712 * q), bottom = top + 80 + height;
            var b = view.yellowBox;
            Place(b[0], left, top, 80, 80); Place(b[1], left + 80, top, width, 80); Place(b[2], right, top, 80, 80);
            Place(b[3], left, top + 80, 80, height); Place(b[4], left + 80, top + 80, width, height); Place(b[5], right, top + 80, 80, height);
            Place(b[6], left, bottom, 80, 80); Place(b[7], left + 80, bottom, width, 80); Place(b[8], right, bottom, 80, 80);
            view.contents.alpha = (float)Math.Min(1, Math.Max(0, (t - ContentsDelayMs) / ContentsMs));
        }

        static void Place(Image image, float x, float y, float width, float height)
        {
            var rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            image.enabled = width > 0.01f && height > 0.01f;
        }

        void ShowCourse(DanCourseEntry entry)
        {
            var course = entry.Course;
            view.frame.sprite = view.frames[Mathf.Clamp(course.Color, 0, view.frames.Length - 1)];
            view.horiName.text = Vertical(course.Title(language));
            if (entry.IsGaiden)
            {
                // The 外伝 plate: no songs or exams, the hint where the course list would be.
                foreach (var row in view.songs) row.root.gameObject.SetActive(false);
                view.exams.gameObject.SetActive(false);
                view.error.gameObject.SetActive(true);
                view.error.text = "段位コードを入力して、一覧にない段位に挑戦できます。\nドンでコードを入力";
                return;
            }
            for (int i = 0; i < view.songs.Length; i++)
            {
                var row = view.songs[i];
                bool shown = i < course.Songs.Count;
                row.root.gameObject.SetActive(shown);
                if (shown) ShowSong(row, i, course.Songs[i], entry.IsPlayable ? entry.Songs[i] : null);
            }
            view.error.gameObject.SetActive(!entry.IsPlayable);
            view.error.text = entry.Error ?? "";
            view.exams.gameObject.SetActive(entry.IsPlayable);
            if (entry.IsPlayable) ShowExams(course, DanRecordStore.Shared != null ? DanRecordStore.Shared.ExamBests(DanRecordStore.Key(entry), course) : null);
        }

        void ShowSong(DanSelectView.SongRowView row, int index, DanSongRef reference, DanResolvedSong resolved)
        {
            int genre = resolved?.Song != null ? resolved.Song.genre : 0;
            row.banner.sprite = view.genreBanners[GenreBannerOrder[Mathf.Clamp(genre, 0, GenreBannerOrder.Length - 1)]];
            row.label.sprite = view.songLabels[Math.Min(index, view.songLabels.Length - 1)];
            row.difficulty.sprite = view.difficulties[Mathf.Clamp((int)reference.Difficulty, 0, view.difficulties.Length - 1)];
            string level = resolved != null && resolved.Level > 0 ? resolved.Level.ToString() : "";
            for (int j = 0; j < row.level.Length; j++)
            {
                bool on = j < level.Length;
                row.level[j].enabled = on;
                if (!on) continue;
                row.level[j].sprite = view.levelDigits[level[j] - '0'];
                // dan_level_counter_margin 34: the digits centred on x 572 (+18, half a digit).
                row.level[j].rectTransform.anchoredPosition = new Vector2(572 - level.Length * 34 / 2f + j * 34, row.level[j].rectTransform.anchoredPosition.y);
            }
            string title = reference.Title, subtitle = reference.Subtitle;
            if (resolved?.Song != null)
            {
                // Read once per song: scrolling the ribbon would otherwise read every chart again.
                if (!displayInfo.TryGetValue(resolved.Song, out var info)) displayInfo[resolved.Song] = info = resolved.Song.ReadDisplayInfo();
                title = info.Title; subtitle = info.Subtitle;
            }
            row.title.text = reference.Hidden ? "？？？" : title;
            row.subtitle.text = reference.Hidden ? "" : subtitle;
            row.title.Squeeze(1140);
            row.subtitle.Squeeze(1140);
        }

        // PyTaikoGreen draw_exam_grid (Nijiiro sets dan_exam_grid): the first gauge exam on the left,
        // up to three more on the right, each with one segment for the course or one per song.
        // bests: DanRecordStore.ExamBests, or null when nothing is recorded.
        void ShowExams(DanCourse course, int?[][] bests)
        {
            var gauge = course.Exams.FirstOrDefault(e => e.Type == DanExamType.Gauge);
            int gaugeIndex = course.Exams.IndexOf(gauge);
            view.gauge.gameObject.SetActive(gauge != null);
            if (gauge != null)
            {
                view.gaugeIcon.sprite = view.examIcons[(int)DanExamType.Gauge];
                view.gaugeCaption.text = Caption(gauge, gauge.Course.Red);
                bestLines.ShowGauge(BestText(gauge, bests, gaugeIndex, 0));
            }
            var rest = course.Exams.Where(e => e != gauge).Take(view.examRows.Length).ToList();
            for (int i = 0; i < view.examRows.Length; i++)
            {
                var row = view.examRows[i];
                bool shown = i < rest.Count;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var exam = rest[i];
                int examIndex = course.Exams.IndexOf(exam);
                row.icon.sprite = view.examIcons[(int)exam.Type];
                int segments = exam.PerSong ? Math.Min(course.Songs.Count, row.segments.Length) : 1;
                for (int k = 0; k < row.segments.Length; k++)
                {
                    bool on = k < segments;
                    row.segments[k].gameObject.SetActive(on);
                    row.captions[k].gameObject.SetActive(on);
                    bestLines.ShowRow(i, k, on ? BestText(exam, bests, examIndex, k) : null);
                    if (!on) continue;
                    row.segments[k].sprite = view.segmentLabels[exam.PerSong ? Math.Min(k + 1, view.segmentLabels.Length - 1) : 0];
                    var border = exam.BorderFor(exam.PerSong ? k : -1);
                    row.captions[k].text = border is DanBorder b ? Caption(exam, b.Red) : "―";
                }
            }
        }

        // The 自己ベスト under an exam's segment: the recorded best, "0" where no attempt counted it
        // (as the arcade shows), the gauge with its ％.
        static string BestText(DanExam exam, int?[][] bests, int exam_index, int segment)
        {
            int value = bests != null && exam_index >= 0 && exam_index < bests.Length && segment < bests[exam_index].Length ? bests[exam_index][segment] ?? 0 : 0;
            return exam.Type == DanExamType.Gauge ? value + " %" : value.ToString();
        }

        // skin_config dan_exam_text_* (ja), as the rest of the Nijiiro screens are: "100 ％以上", "5 未満".
        static string Caption(DanExam exam, int red)
        {
            string range = exam.Range == DanExamRange.Less ? "未満" : "以上";
            return exam.Type == DanExamType.Gauge ? $"{red} ％{range}" : $"{red} {range}";
        }

        void ShowConfirm(double now)
        {
            // Still shown under the curtain once はい starts the course.
            bool open = Flow.State == DanSelectFlow.Phase.Confirm || Flow.State == DanSelectFlow.Phase.Options || Flow.State == DanSelectFlow.Phase.Started;
            view.confirm.gameObject.SetActive(open);
            if (!open) return;
            view.confirm.alpha = (float)Math.Min(1, Math.Max(0, (now - Flow.ConfirmOpenedAt - ConfirmDelayMs) / ConfirmFadeMs));
            float pulse = CursorAlpha(now);
            var cursor = Flow.Cursor;
            // SkinUi.Alpha enables what it fades: only the chosen one's highlight pulses.
            view.yesHighlight.Alpha(cursor == DanSelectFlow.Choice.Yes ? pulse : 0);
            view.noHighlight.Alpha(cursor == DanSelectFlow.Choice.No ? pulse : 0);
            view.optionHighlight.Alpha(cursor == DanSelectFlow.Choice.Option ? pulse : 0);
            view.yesOutline.enabled = cursor == DanSelectFlow.Choice.Yes;
            view.noOutline.enabled = cursor == DanSelectFlow.Choice.No;
        }
    }
}
