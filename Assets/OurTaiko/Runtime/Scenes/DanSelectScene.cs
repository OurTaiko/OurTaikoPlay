using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public IReadOnlyList<DanCourseEntry> Courses => courses;
        public bool HasLeft { get; private set; }
        public OptionMenu Options { get; private set; }
        // The doors play once the cover has opened; the screen takes input after them.
        public bool FullIntro { get; private set; }
        public bool IntroDone => intro == null || !intro.IsPlaying && !double.IsNaN(introStartedAt);

        readonly List<DanCourseEntry> courses = new List<DanCourseEntry>();
        readonly Dictionary<SongDefinition, SongInfo> displayInfo = new Dictionary<SongDefinition, SongInfo>();
        SceneSwitcher switcher;
        OptionPanel optionPanel;
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
            switcher.DanIndexMax = courses.Count > 0 ? courses.Max(c => c.Course.DanIndex) : -1;
            int start = switcher.SelectedDan != null ? Math.Max(0, courses.FindIndex(c => Same(c, switcher.SelectedDan))) : 0;
            Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, start);
            ribbonFrom = ribbonTo = Flow.Selected;
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

        // Local courses first (each library root in order), then each connected server's, then the
        // server stand-in's.
        void Load()
        {
            var library = LocalSongLibrary.EnsureInstance();
            var local = library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).Distinct().ToList();
            var online = Online.OnlineManager.EnsureInstance();
            var resolver = new DanCourseResolver(local, (server, id) => FindServerSong(online, server, id));
            courses.AddRange(DanCourseLoader.LoadLocal(library.Roots, resolver));
            if (online.Client != null)
                foreach (var (server, dans) in online.Client.Dans) courses.AddRange(DanCourseLoader.LoadServer(server, dans, resolver));
            courses.AddRange(DanCourseLoader.LoadServerTest(Path.Combine(GamePaths.Root, DanCourseLoader.ServerTestFolder), resolver));
            foreach (var entry in courses)
            {
                if (entry.Error != null) Debug.LogWarning($"Dan course {entry.Path}: {entry.Error}");
                foreach (string warning in entry.Course.Warnings) Debug.LogWarning($"Dan course {entry.Path}: {warning}");
            }
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
            if (Flow.State == DanSelectFlow.Phase.Options)
            {
                if (Options == null || Options.IsConfirmed) return;
                if (Pressed(InputKey.Back)) { CloseOptions(); return; }
                if (Pressed(InputKey.LeftKa, InputKey.MenuLeft)) ChangeOption(-1);
                else if (Pressed(InputKey.RightKa, InputKey.MenuRight)) ChangeOption(+1);
                else if (Pressed(InputKey.LeftDon, InputKey.RightDon, InputKey.Confirm)) { Play(sfx, don, true); Options.Confirm(); }
                return;
            }
            if (Pressed(InputKey.Back)) { Back(); return; }
            if (Pressed(InputKey.LeftDon, InputKey.RightDon, InputKey.Confirm)) Don();
            else if (Pressed(InputKey.LeftKa, InputKey.MenuLeft, InputKey.MenuUp)) Ka(-1);
            else if (Pressed(InputKey.RightKa, InputKey.MenuRight, InputKey.MenuDown)) Ka(1);
        }

        static bool Pressed(params InputKey[] keys) => keys.Any(InputManager.GetKeyDown);

        // A drum face hit: the confirmation opens on a playable course, or decides there.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft || !IntroDone) return;
            double now = Now;
            if (Flow.State == DanSelectFlow.Phase.Browsing)
            {
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
                Debug.LogWarning("DanPlayScene is not in the build.");
                Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, Flow.Selected);
                return;
            }
            if (switcher.PlayDan(entry)) HasLeft = true;
            else Flow = new DanSelectFlow(courses.Count, i => courses[i].IsPlayable, Flow.Selected);
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
                string name = Vertical(entry.Course.Title(language));
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
            view.horiName.text = course.Title(language);
            view.horiName.Squeeze(240);
            ShowRecord(entry);
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
            if (entry.IsPlayable) ShowExams(course);
        }

        // The best of the attempts played for real: verdict, score and how far the course went.
        void ShowRecord(DanCourseEntry entry)
        {
            if (view.record == null) return;
            var best = entry.IsPlayable ? DanRecordStore.Shared.Get(DanRecordStore.Key(entry)) : null;
            view.record.gameObject.SetActive(best.HasValue);
            if (!best.HasValue) return;
            var b = best.Value;
            view.record.text = $"最高記録\n{DanResultFlow.VerdictName(b.Verdict)}\n{b.Score:N0}点　{b.Arrival}曲";
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
        void ShowExams(DanCourse course)
        {
            var gauge = course.Exams.FirstOrDefault(e => e.Type == DanExamType.Gauge);
            view.gauge.gameObject.SetActive(gauge != null);
            if (gauge != null)
            {
                view.gaugeIcon.sprite = view.examIcons[(int)DanExamType.Gauge];
                view.gaugeCaption.text = Caption(gauge, gauge.Course.Red);
            }
            var rest = course.Exams.Where(e => e != gauge).Take(view.examRows.Length).ToList();
            for (int i = 0; i < view.examRows.Length; i++)
            {
                var row = view.examRows[i];
                bool shown = i < rest.Count;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var exam = rest[i];
                row.icon.sprite = view.examIcons[(int)exam.Type];
                int segments = exam.PerSong ? Math.Min(course.Songs.Count, row.segments.Length) : 1;
                for (int k = 0; k < row.segments.Length; k++)
                {
                    bool on = k < segments;
                    row.segments[k].gameObject.SetActive(on);
                    row.captions[k].gameObject.SetActive(on);
                    if (!on) continue;
                    row.segments[k].sprite = view.segmentLabels[exam.PerSong ? Math.Min(k + 1, view.segmentLabels.Length - 1) : 0];
                    var border = exam.BorderFor(exam.PerSong ? k : -1);
                    row.captions[k].text = border is DanBorder b ? Caption(exam, b.Red) : "―";
                }
            }
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
