using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro single-player song select (scenes/song_select.cpp + Scripts/song_select/song_select.lua):
    // the vertical song-board wheel, the course-select panel and the 演奏オプション panel behind the option
    // button. Folders, search, sorting, the standalone neiro panel, dan boards and 2P are not ported.
    public sealed class SongSelectScene : MonoBehaviour
    {
        public enum State { Browsing, CourseSelect, Decided }

        [Header("Songs")]
        public SongDefinition[] songs;
        public TMP_FontAsset font;
        public Material outlineMaterial;

        [Header("Stage")]
        public RectTransform wheel, coursePanel;
        public Image[] backgroundTiles; // previous genre x2, current genre x2

        [Header("Wheel art")]
        public Sprite[] genreBackgrounds, boards;
        public Sprite cursorGlow, branch;
        public Sprite[] plates, levels, stars, crownClear, crownFullCombo, crownDonderful;

        [Header("Course select art")]
        public Sprite[] backboards, courseBoards, courseMarks, smallCrowns, smallStars;
        public Sprite courseFrame, playerBalloon, backButton, optionButton, buttonGlow, levelBar, levelDot, courseBranch, autoIcon;
        public Texture2D uraChangeToUra, uraChangeToOni;

        [Header("Play options")]
        public OptionPanelArt optionArt;

        [Header("Player")]
        public NameplateView nameplatePrefab;

        [Header("Timelines")]
        public TextAsset songBoardTimeline, cursorGlowTimeline, uraLoopTimeline;

        [Header("Audio")]
        public AudioSource bgm, preview, sfx, voice;
        public AudioClip don, ka, uraSwitch, voiceEnter, voiceStartSong;

        // Board geometry (common_song_select_main_song_board): 960x352 = 56 cap / 240 centre / 56 cap.
        const float CapHeight = 56, ClosedCentre = 52, OpenCentre = 240, GlowOverhang = 24;
        const float CentreY = 540, RowPitch = 135, ExpandGap = 120, CrossX = 960, RowCurve = 40;
        const double MoveMs = 166, OpenHoldMs = 61 / 120.0 * 1000, OpenGrowMs = 233, CloseMs = 13 / 0.06;
        const double CourseExitMoveMs = 500, CourseEnterMoveMs = 800, BoardFadeMs = 166;
        const double CourseFadeDelayMs = 400, CourseFadeMs = 483, GenreFadeMs = 200, BackgroundLoopMs = 15000;
        const double BgmResumeMs = 330, UraChangeMs = 1500, UraSwapMs = 450;
        const int UraCells = 90;
        static readonly float[] CourseSlotX = { 745, 960, 1175, 1390 };
        const float BackX = 440, OptionX = 572, ButtonY = 462, CursorY = 370, CourseBoardY = 583;
        static readonly string[] ChipNames = { "かんたん", "ふつう", "むずかしい", "おに", "おに(裏)" };
        // song_select.lua BOARD_OUTLINE: the board title outline is the genre colour.
        static readonly Color32[] GenreOutline = {
            new Color32(18, 72, 76, 255), new Color32(18, 72, 76, 255), new Color32(155, 24, 99, 255),
            new Color32(78, 29, 118, 255), new Color32(150, 31, 0, 255), new Color32(101, 67, 42, 255),
            new Color32(20, 79, 20, 255), new Color32(165, 50, 0, 255), new Color32(38, 52, 73, 255),
            new Color32(18, 72, 76, 255),
        };

        public State Phase { get; private set; } = State.Browsing;
        public int Focused { get; private set; }
        public DifficultyCursor Cursor { get; private set; }
        public bool AutoPlay => PlayOptions.Shared.auto;
        public bool IsOptionPanelOpen => optionPanel != null && optionPanel.IsOpen;
        public OptionMenu OptionMenu => optionPanel?.Menu;
        public SongDefinition FocusedSong => songs[Focused];
        public double CourseFade => Phase == State.Browsing ? 0 : Clamp01((Now - courseEnteredAt - CourseFadeDelayMs) / CourseFadeMs);
        public bool IsPreviewPlaying => preview != null && preview.isPlaying;

        sealed class Plate { public Difficulty Difficulty; public CanvasGroup Group; }
        sealed class Board
        {
            public SongDefinition Song;
            public SongInfo Info;
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Glow, Panel, Crown;
            public TextMeshProUGUI Title, Subtitle;
            public CanvasGroup Contents;
            public readonly List<Plate> Plates = new List<Plate>();
            public double Position, MoveFrom, MoveTo, MoveStart = -1, MoveDuration;
            public double Cross, CrossFrom, CrossTo;
            public double OpenStart = -1, CloseStart = -1, Hold;
            public double FadeStart = -1, FadeFrom = 1, FadeTo = 1;
        }
        sealed class CourseCard
        {
            public Image Board, Crown, Star, Level, Bar, Branch;
            public Image[] Dots;
            public TextMeshProUGUI Name;
        }

        readonly List<Board> wheelBoards = new List<Board>();
        readonly CourseCard[] cards = new CourseCard[4];
        LumenClip songBoard, glowClip, uraLoop;
        SceneSwitcher switcher;
        double startedAt, genreChangedAt = -10000, courseEnteredAt, bgmResumeAt = -1, uraChangedAt = -1;
        int previousGenre, currentGenre;
        bool uraChangeToUraSide, previewStarted, started;
        Sprite[] uraToUraCells, uraToOniCells;
        Image mark, backboard, back, option, auto, frame, glow, balloon, uraChange;
        TextMeshProUGUI header, headerSub;
        OptionPanel optionPanel;

        double Now => Time.realtimeSinceStartupAsDouble * 1000 - startedAt;
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            startedAt = Time.realtimeSinceStartupAsDouble * 1000;
            songBoard = LumenClip.Parse(songBoardTimeline.text);
            glowClip = LumenClip.Parse(cursorGlowTimeline.text);
            uraLoop = LumenClip.Parse(uraLoopTimeline.text);
            switcher.SceneChanging += OnSceneChanging;
            for (int i = 0; i < songs.Length; i++) wheelBoards.Add(CreateBoard(songs[i]));
            int remembered = Array.IndexOf(songs, switcher.SelectedSong);
            Focused = remembered >= 0 ? remembered : 0;
            currentGenre = previousGenre = FocusedSong.genre;
            BuildCoursePanel();
            BuildNameplate();
            // Slice the 90-cell Oni/Ura change sheets up front so the first flip does not hitch.
            UraFrames(ref uraToUraCells, uraChangeToUra);
            UraFrames(ref uraToOniCells, uraChangeToOni);
            SetPositions(true, 0);
            // SecondLoading: the initially focused board opens at once, without the 508 ms hold.
            OpenFocused(holdMs: 0);
            coursePanel.gameObject.SetActive(false);
        }

        void Start()
        {
            bgm.loop = true;
            bgm.Play();
            PlayVoice(voiceEnter);
            started = true;
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            bgm.Stop(); preview.Stop();
            if (IsOptionPanelOpen) PlayOptions.Shared.Save();
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (!started) return;
            double now = Now;
            HandleInput();
            if (Phase == State.Decided && !voice.isPlaying && !switcher.IsSwitching) StartSong();
            UpdatePreview(now);
            DrawBackground(now);
            for (int i = 0; i < wheelBoards.Count; i++) DrawBoard(wheelBoards[i], i == Focused, now);
            if (Phase != State.Browsing) DrawCoursePanel(now);
            // Player::update: the options are saved once the panel has slid out.
            if (optionPanel.Draw(now)) PlayOptions.Shared.Save();
        }

        void HandleInput()
        {
            if (switcher.IsInputBlocked) return;
            if (InputManager.GetKeyDown(InputKey.Back))
            {
                if (IsOptionPanelOpen) CloseOptions();
                else switcher.SwitchScene(SceneSwitcher.MenuScene);
                return;
            }
            bool leftKa = InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft);
            bool rightKa = InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight);
            bool donHit = InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm);
            if (InputManager.GetKeyDown(InputKey.ToggleAuto) && Phase != State.Decided) ToggleAuto();
            if (leftKa) Left();
            else if (rightKa) Right();
            else if (donHit) Confirm();
        }

        public void Left()
        {
            if (!AcceptsInput()) return;
            sfx.PlayOneShot(ka);
            if (IsOptionPanelOpen) ChangeOption(-1);
            else if (Phase == State.Browsing) Navigate(-1);
            else Cursor.Left();
        }

        public void Right()
        {
            if (!AcceptsInput()) return;
            sfx.PlayOneShot(ka);
            if (IsOptionPanelOpen) ChangeOption(+1);
            else if (Phase == State.Browsing) Navigate(+1);
            else if (Cursor.Right())
            {
                // toggle_ura_mode: the oni card plays change_ura / change_oni for 90 frames.
                sfx.PlayOneShot(uraSwitch);
                uraChangedAt = Now; uraChangeToUraSide = Cursor.IsUra;
            }
        }

        public void Confirm()
        {
            if (!AcceptsInput()) return;
            sfx.PlayOneShot(don);
            if (IsOptionPanelOpen) { optionPanel.Menu.Confirm(); return; }
            if (Phase == State.Browsing) { EnterCourseSelect(); return; }
            switch (Cursor.Selected)
            {
                case Difficulty.Back: ExitCourseSelect(); break;
                case Difficulty.Modifier: OpenOptions(); break;
                default:
                    Phase = State.Decided;
                    switcher.LastDifficulty = (int)Cursor.Selected;
                    PlayVoice(voiceStartSong);
                    break;
            }
        }

        void PlayVoice(AudioClip clip)
        {
            voice.Stop();
            voice.clip = clip;
            if (clip != null) voice.Play();
        }

        bool AcceptsInput()
        {
            if (switcher.IsInputBlocked || Phase == State.Decided) return false;
            // The course panel ignores input while it is still fading in.
            return Phase != State.CourseSelect || CourseFade >= 1;
        }

        public void ToggleAuto()
        {
            var options = PlayOptions.Shared;
            options.auto = !options.auto;
            options.Save();
        }

        void StartSong()
        {
            var course = wheelBoards[Focused].Info.Course(Cursor.Selected);
            switcher.Play(FocusedSong, course.Course, AutoPlay);
        }

        // ---------------------------------------------------------------- play options

        // SongSelectPlayer::handle_input_selecting: don on the option button opens ModifierSelector.
        void OpenOptions()
        {
            optionPanel.Open(PlayOptions.Shared, Now);
            PlayVoice(optionArt.voice);
        }

        void CloseOptions()
        {
            if (!AcceptsInput() || !IsOptionPanelOpen || optionPanel.IsClosing) return;
            sfx.PlayOneShot(don);
            optionPanel.Close(Now);
        }

        void ChangeOption(int direction)
        {
            var menu = optionPanel.Menu;
            if (!(direction < 0 ? menu.Left() : menu.Right())) return;
            optionPanel.Changed(direction, Now);
            // step_neiro previews the new set's don; 無音 plays nothing.
            if (menu.Current == OptionRow.Neiro && optionArt.hitSounds != null
                && optionArt.hitSounds.TryGet(menu.Options.neiro, out var preview, out _))
                sfx.PlayOneShot(preview);
        }

        void OnOptionRowTapped(int row, int direction)
        {
            if (!AcceptsInput() || !IsOptionPanelOpen || optionPanel.IsClosing) return;
            var menu = optionPanel.Menu;
            if (direction == 0)
            {
                if (menu.Index == row) Confirm();
                else { sfx.PlayOneShot(ka); menu.Select(row); }
                return;
            }
            menu.Select(row);
            if (direction < 0) Left(); else Right();
        }

        // ---------------------------------------------------------------- wheel

        public void Navigate(int delta)
        {
            if (wheelBoards.Count == 0) return;
            var previous = wheelBoards[Focused];
            if (previous.OpenStart >= 0) { previous.CloseStart = Now; previous.OpenStart = -1; }
            Focused = ((Focused + delta) % songs.Length + songs.Length) % songs.Length;
            SetPositions(false, MoveMs);
            OpenFocused(OpenHoldMs);
            previousGenre = currentGenre; currentGenre = FocusedSong.genre; genreChangedAt = Now;
            StopPreview();
        }

        void OpenFocused(double holdMs)
        {
            var board = wheelBoards[Focused];
            board.OpenStart = Now; board.Hold = holdMs; board.CloseStart = -1;
        }

        // Navigator::set_positions (vertical gallery): pitch 135, +-120 around the open board,
        // each row 40 px further right; a jump of a whole screen snaps instead of sliding.
        void SetPositions(bool snap, double duration)
        {
            int count = wheelBoards.Count;
            for (int i = 0; i < count; i++)
            {
                double offset = i - Focused;
                if (offset > count / 2.0) offset -= count;
                else if (offset < -count / 2.0) offset += count;
                double position = CentreY + offset * RowPitch + Math.Sign(offset) * ExpandGap;
                double cross = CrossX + offset * RowCurve;
                var board = wheelBoards[i];
                if (snap || Math.Abs(position - board.Position) >= 1080) { board.Position = position; board.Cross = cross; board.MoveStart = -1; }
                else MoveBoard(board, position, cross, duration);
            }
        }

        void MoveBoard(Board board, double position, double cross, double duration)
        {
            board.MoveFrom = board.Position; board.MoveTo = position;
            board.CrossFrom = board.Cross; board.CrossTo = cross;
            board.MoveStart = Now; board.MoveDuration = duration;
        }

        void FadeBoard(Board board, double to)
        {
            board.FadeFrom = BoardFade(board, Now); board.FadeTo = to; board.FadeStart = Now;
        }

        static double BoardFade(Board board, double now)
            => board.FadeStart < 0 ? board.FadeTo : board.FadeFrom + (board.FadeTo - board.FadeFrom) * Clamp01((now - board.FadeStart) / BoardFadeMs);

        // ---------------------------------------------------------------- course select

        void EnterCourseSelect()
        {
            var board = wheelBoards[Focused];
            var courses = board.Info.Courses.Select(c => c.Difficulty).ToList();
            Cursor = new DifficultyCursor(courses, Cursor != null && Cursor.IsUra, switcher.LastDifficulty);
            Phase = State.CourseSelect;
            courseEnteredAt = Now;
            uraChangedAt = -1;
            // Navigator::enter_diff_select: the other on-screen boards leave by 150 px and fade out.
            for (int i = 0; i < wheelBoards.Count; i++)
            {
                var other = wheelBoards[i];
                if (i == Focused || other.Position < -100 || other.Position > 1180) continue;
                MoveBoard(other, other.Position < CentreY ? -150 : 1080 + 150, other.Cross, CourseEnterMoveMs);
                FadeBoard(other, 0);
            }
            FillCoursePanel(board);
            coursePanel.gameObject.SetActive(true);
        }

        void ExitCourseSelect()
        {
            Phase = State.Browsing;
            coursePanel.gameObject.SetActive(false);
            SetPositions(false, CourseExitMoveMs);
            foreach (var board in wheelBoards) FadeBoard(board, 1);
            // Course back replays select_on immediately on the focused board.
            OpenFocused(holdMs: 0);
        }

        // ---------------------------------------------------------------- preview / bgm

        void UpdatePreview(double now)
        {
            var board = wheelBoards[Focused];
            bool open = Phase == State.Browsing && board.OpenStart >= 0 && now - board.OpenStart >= board.Hold + OpenGrowMs;
            if (open && !previewStarted && board.Song.music != null)
            {
                previewStarted = true;
                bgm.Stop();
                bgmResumeAt = -1;
                preview.clip = board.Song.music;
                preview.time = Mathf.Clamp((float)board.Info.DemoStart, 0, Mathf.Max(0, board.Song.music.length - 0.1f));
                preview.Play();
            }
            if (bgmResumeAt >= 0 && now >= bgmResumeAt && !previewStarted)
            {
                bgmResumeAt = -1;
                bgm.Play();
            }
        }

        // SongBox::close_box: stop the preview; the select bgm comes back 330 ms later.
        void StopPreview()
        {
            if (!previewStarted) return;
            previewStarted = false;
            preview.Stop();
            bgmResumeAt = Now + BgmResumeMs;
        }

        // ---------------------------------------------------------------- drawing

        void DrawBackground(double now)
        {
            // navigator background: 2880-px genre strip, 1920 px per 15 s; the new genre fades in over 200 ms.
            float move = (float)(now % BackgroundLoopMs / BackgroundLoopMs * 1920);
            float change = (float)Clamp01((now - genreChangedAt) / GenreFadeMs);
            for (int i = 0; i < 2; i++)
            {
                backgroundTiles[i].sprite = genreBackgrounds[previousGenre];
                backgroundTiles[i + 2].sprite = genreBackgrounds[currentGenre];
                backgroundTiles[i + 2].Alpha(change);
                backgroundTiles[i].rectTransform.TopLeft(i * 2880 - move, 0);
                backgroundTiles[i + 2].rectTransform.TopLeft(i * 2880 - move, 0);
            }
        }

        void DrawBoard(Board board, bool focused, double now)
        {
            if (board.MoveStart >= 0)
            {
                double p = SkinUi.CubicOut((now - board.MoveStart) / board.MoveDuration);
                board.Position = board.MoveFrom + (board.MoveTo - board.MoveFrom) * p;
                board.Cross = board.CrossFrom + (board.CrossTo - board.CrossFrom) * p;
                if (now - board.MoveStart >= board.MoveDuration) board.MoveStart = -1;
            }
            bool hidden = focused && Phase != State.Browsing;
            float fade = (float)BoardFade(board, now);
            board.Group.alpha = hidden ? 0 : fade;
            board.Root.gameObject.SetActive(!hidden && board.Position > -400 && board.Position < 1480);
            if (!board.Root.gameObject.activeSelf) return;
            board.Root.Center((float)board.Cross, (float)board.Position);

            // select_on / select_off of the song board clip (anim/song_board): board centre sy and
            // the song_kanban_info alpha, after the 508 ms pre-growth hold.
            double frame = -1;
            if (focused && board.OpenStart >= 0)
            {
                double t = now - board.OpenStart - board.Hold;
                if (t >= 0) frame = songBoard.Label("select_on").GetValueOrDefault(5) + Math.Min(t, OpenGrowMs) * 0.06;
            }
            else if (board.CloseStart >= 0)
            {
                double e = now - board.CloseStart;
                if (e < CloseMs) frame = songBoard.Label("select_off").GetValueOrDefault(30) + e * 0.06;
                else board.CloseStart = -1;
            }
            double pb = 0, ia = 0;
            if (frame >= 0)
            {
                double sy = songBoard.Get("instance_board_center", frame, "sy", 0.2168);
                pb = Clamp01((sy - 0.2168) / (1 - 0.2168));
                ia = Clamp01(songBoard.Get("song_kanban_info", frame, "a", pb >= 1 ? 1 : 0));
            }
            float centre = (float)(ClosedCentre + (OpenCentre - ClosedCentre) * pb);
            board.Panel.rectTransform.sizeDelta = new Vector2(960, centre + CapHeight * 2);
            board.Glow.rectTransform.sizeDelta = new Vector2(1024, centre + CapHeight * 2 + GlowOverhang * 2);
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            board.Glow.Alpha((float)(pb * pulse));
            board.Title.rectTransform.Center(0, (float)(-70 * pb));
            board.Contents.alpha = (float)ia;
            board.Subtitle.Alpha((float)ia);
            DrawPlates(board, now);
            DrawCrown(board, (float)pb, (float)(pb > 0 ? ia : 1));
        }

        void DrawPlates(Board board, double now)
        {
            if (board.Contents.alpha <= 0) return;
            // song_kanban_info 'ura': a 180-frame loop cross-fading the oni and ura chips.
            double f = uraLoop.First + now * 0.06 % Math.Max(1, uraLoop.Last - uraLoop.First + 1);
            float ura = (float)uraLoop.Get("ura/text_course_ura", f, "a", 0);
            float oni = (float)uraLoop.Get("oni/text_course_oni", f, "a", 1);
            bool both = board.Info.Has(Difficulty.Oni) && board.Info.Has(Difficulty.Ura);
            foreach (var plate in board.Plates)
                plate.Group.alpha = !both ? 1 : plate.Difficulty == Difficulty.Ura ? ura : plate.Difficulty == Difficulty.Oni ? oni : 1;
        }

        // SetCrown / SearchAnyCrown: the highest crowned course; 72 px at (-425,-114) open,
        // 0.75 scale at (-425,-27) closed.
        void DrawCrown(Board board, float p, float fade)
        {
            Crown best = Crown.None; Difficulty course = Difficulty.Easy;
            foreach (var info in board.Info.Courses)
            {
                var record = ScoreStore.Shared.Get(board.Song.name, info.Difficulty);
                if (record != null && record.crown != Crown.None) { best = record.crown; course = info.Difficulty; }
            }
            if (best == Crown.None) { board.Crown.enabled = false; return; }
            var set = best == Crown.DonderfulCombo ? crownDonderful : best == Crown.FullCombo ? crownFullCombo : crownClear;
            board.Crown.sprite = set[(int)course];
            float size = 72 * (0.75f + 0.25f * p);
            board.Crown.rectTransform.sizeDelta = new Vector2(size, size);
            board.Crown.rectTransform.Center(-425, -27 + (-114 + 27) * p);
            board.Crown.Alpha(fade);
        }

        void DrawCoursePanel(double now)
        {
            float fade = (float)CourseFade;
            var group = coursePanel.GetComponent<CanvasGroup>();
            group.alpha = fade;
            var board = wheelBoards[Focused];
            var selected = Cursor.Selected;
            int column = selected >= Difficulty.Easy ? Math.Min((int)Difficulty.Oni, (int)selected) : -1;
            mark.enabled = column >= 0;
            if (column >= 0) mark.sprite = courseMarks[(int)selected];

            double ura = uraChangedAt < 0 ? -1 : now - uraChangedAt;
            if (ura >= UraChangeMs) { uraChangedAt = -1; ura = -1; }
            bool oniShowsUra = Cursor.IsUra;
            if (ura >= 0 && ura < UraSwapMs) oniShowsUra = !oniShowsUra;
            for (int i = 0; i < 4; i++)
                FillCard(cards[i], board, i == 3 && oniShowsUra ? Difficulty.Ura : (Difficulty)i, i == 3 && ura >= 0);
            uraChange.enabled = ura >= 0;
            if (ura >= 0)
            {
                var cells = uraChangeToUraSide ? UraFrames(ref uraToUraCells, uraChangeToUra) : UraFrames(ref uraToOniCells, uraChangeToOni);
                uraChange.sprite = cells[Math.Min(UraCells - 1, (int)(ura / UraChangeMs * UraCells))];
            }
            auto.enabled = AutoPlay;

            // draw_selector: the course frame / button glow under the boards, the 1P bubble above.
            float x = column >= 0 ? CourseSlotX[column] : selected == Difficulty.Modifier ? OptionX : BackX;
            bool hideForUra = column == 3 && ura >= 0;
            frame.enabled = column >= 0 && !hideForUra;
            glow.enabled = column < 0;
            balloon.enabled = !hideForUra;
            frame.rectTransform.Center(x, CourseBoardY);
            glow.rectTransform.Center(x, ButtonY);
            balloon.rectTransform.Center(x, CursorY);
        }

        Sprite[] UraFrames(ref Sprite[] cells, Texture2D sheet)
        {
            if (cells != null) return cells;
            cells = new Sprite[UraCells];
            for (int i = 0; i < UraCells; i++)
            {
                var rect = new Rect(i % 10 * 340, sheet.height - (i / 10 + 1) * 400, 340, 400);
                cells[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), 100);
            }
            return cells;
        }

        void FillCoursePanel(Board board)
        {
            backboard.sprite = backboards[board.Song.genre];
            header.text = board.Info.Title;
            header.Squeeze(1000);
            headerSub.text = board.Info.Subtitle;
            headerSub.Squeeze(1000);
        }

        void FillCard(CourseCard card, Board board, Difficulty difficulty, bool changing)
        {
            var info = board.Info.Course(difficulty);
            bool has = info != null;
            card.Board.sprite = courseBoards[(int)difficulty];
            card.Board.color = new Color(1, 1, 1, has ? 1 : 0.4f);
            bool details = has && !changing;
            card.Name.enabled = has;
            card.Name.text = ChipNames[(int)difficulty];
            foreach (var image in new[] { card.Crown, card.Star, card.Level, card.Bar }) image.enabled = details;
            card.Branch.enabled = details && info.IsBranching;
            for (int k = 0; k < card.Dots.Length; k++) card.Dots[k].enabled = details && k < Math.Min(10, info.Level);
            if (!details) return;
            var record = ScoreStore.Shared.Get(board.Song.name, difficulty);
            card.Crown.sprite = smallCrowns[(int)(record?.crown ?? Crown.None)];
            card.Level.sprite = smallStars[Mathf.Clamp(info.Level, 1, 11)];
        }

        // ---------------------------------------------------------------- construction

        Board CreateBoard(SongDefinition song)
        {
            var board = new Board { Song = song, Info = song.ReadInfo() };
            board.Root = SkinUi.Rect(song.name, wheel);
            board.Group = board.Root.gameObject.AddComponent<CanvasGroup>();
            board.Group.blocksRaycasts = true;
            board.Glow = SkinUi.Image("CursorGlow", board.Root, cursorGlow, 1024, 0);
            board.Panel = SkinUi.Image("Board", board.Root, boards[song.genre], 960, 0);
            board.Panel.raycastTarget = true;
            board.Panel.gameObject.AddComponent<PointerRelay>().Clicked = () => OnBoardClicked(board);
            var contents = SkinUi.Rect("Contents", board.Root);
            board.Contents = contents.gameObject.AddComponent<CanvasGroup>();
            board.Contents.blocksRaycasts = false;
            CreatePlates(board, contents);
            board.Crown = SkinUi.Image("Crown", board.Root, null, 72, 72);
            board.Crown.enabled = false;
            var outline = GenreOutline[Mathf.Clamp(song.genre, 0, GenreOutline.Length - 1)];
            // main_song_board text_song_title: 42 px, white with a 5 px genre-coloured edge.
            board.Title = SkinUi.Text("Title", board.Root, font, outlineMaterial, 42, outline, 0.3f);
            board.Title.text = board.Info.Title;
            board.Title.Squeeze(860);
            board.Subtitle = SkinUi.Text("Subtitle", board.Root, font, outlineMaterial, 24, outline, 0.3f);
            board.Subtitle.text = board.Info.Subtitle;
            board.Subtitle.Squeeze(860, 0.75f);
            board.Subtitle.rectTransform.Center(0, -28);
            return board;
        }

        // Diff plates at board y +75: easy / normal / hard / (oni|ura), pitch 182, centred.
        void CreatePlates(Board board, RectTransform parent)
        {
            var columns = new List<Difficulty>();
            for (var d = Difficulty.Easy; d <= Difficulty.Hard; d++) if (board.Info.Has(d)) columns.Add(d);
            if (board.Info.Has(Difficulty.Oni) || board.Info.Has(Difficulty.Ura)) columns.Add(Difficulty.Oni);
            for (int i = 0; i < columns.Count; i++)
            {
                float x = (i - (columns.Count - 1) / 2f) * 182;
                if (columns[i] == Difficulty.Oni)
                {
                    if (board.Info.Has(Difficulty.Oni)) CreatePlate(board, parent, Difficulty.Oni, x);
                    if (board.Info.Has(Difficulty.Ura)) CreatePlate(board, parent, Difficulty.Ura, x);
                }
                else CreatePlate(board, parent, columns[i], x);
            }
        }

        void CreatePlate(Board board, RectTransform parent, Difficulty difficulty, float x)
        {
            var info = board.Info.Course(difficulty);
            var root = SkinUi.Rect(difficulty.ToString(), parent);
            root.Center(x, 75);
            var plate = new Plate { Difficulty = difficulty, Group = root.gameObject.AddComponent<CanvasGroup>() };
            int d = (int)difficulty;
            SkinUi.Image("Plate", root, plates[d], 184, 96);
            SkinUi.Image("Star", root, stars[d], 40, 40).rectTransform.Center(27, 0);
            int level = Mathf.Clamp(info.Level, 1, 11);
            SkinUi.Image("Level", root, levels[d * 11 + level - 1], 48, 48).rectTransform.Center(61, 0);
            var label = SkinUi.Text("Course", root, font, outlineMaterial, 18, new Color32(40, 20, 20, 255), 0.25f);
            label.text = ChipNames[d];
            label.rectTransform.Center(-42, 30);
            if (info.IsBranching) SkinUi.Image("Branch", root, branch, 40, 40).rectTransform.Center(-69, -23);
            board.Plates.Add(plate);
        }

        // SongSelectPlayer::draw paints the nameplate over the wheel and under the option panel. CoursePanel
        // holds the cards and the option panel, none of which reach y 908, so the plate goes just below it.
        void BuildNameplate()
        {
            if (nameplatePrefab == null) return;
            var plate = Instantiate(nameplatePrefab, coursePanel.parent);
            plate.name = "Nameplate";
            plate.transform.SetSiblingIndex(coursePanel.GetSiblingIndex());
            plate.Place(14, 908);
        }

        void BuildCoursePanel()
        {
            coursePanel.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            // main_diff: the course mark sits under the backboard; only the part outside it shows.
            mark = SkinUi.Image("CourseMark", coursePanel, courseMarks[0], 680, 680);
            mark.rectTransform.Center(200, 408);
            backboard = SkinUi.Image("Backboard", coursePanel, backboards[0], 1272, 784);
            backboard.rectTransform.Center(960, 440);
            glow = SkinUi.Image("ButtonCursor", coursePanel, buttonGlow, 168, 168);
            frame = SkinUi.Image("CourseCursor", coursePanel, courseFrame, 248, 408);
            back = SkinUi.Image("Back", coursePanel, backButton, 128, 128);
            back.rectTransform.Center(BackX, ButtonY);
            AddClick(back, Difficulty.Back);
            option = SkinUi.Image("Option", coursePanel, optionButton, 128, 128);
            option.rectTransform.Center(OptionX, ButtonY);
            AddClick(option, Difficulty.Modifier);
            auto = SkinUi.Image("AutoPlay", coursePanel, autoIcon, 40, 40);
            auto.rectTransform.Center(OptionX + 44, ButtonY + 44);
            for (int i = 0; i < 4; i++)
            {
                float x = CourseSlotX[i];
                var card = cards[i] = new CourseCard();
                card.Board = SkinUi.Image("Course" + i, coursePanel, courseBoards[i], 200, 360);
                card.Board.rectTransform.Center(x, CourseBoardY);
                AddClick(card.Board, (Difficulty)i);
                // Card children are placed from the 200x360 card's top-left (centre = 100,180).
                card.Crown = SkinUi.Image("Crown", card.Board.transform, smallCrowns[0], 40, 40);
                card.Crown.rectTransform.Center(100 - 58, 180 - 138);
                card.Star = SkinUi.Image("Star", card.Board.transform, smallStars[0], 56, 40);
                card.Star.rectTransform.TopLeft(100 - 40, 180 + 66);
                card.Level = SkinUi.Image("Level", card.Board.transform, smallStars[1], 56, 40);
                card.Level.rectTransform.TopLeft(100 - 4, 180 + 68);
                card.Bar = SkinUi.Image("LevelBar", card.Board.transform, levelBar, 172, 24);
                card.Bar.rectTransform.Center(100, 180 + 116);
                card.Dots = new Image[10];
                for (int k = 0; k < 10; k++)
                {
                    card.Dots[k] = SkinUi.Image("Dot" + k, card.Board.transform, levelDot, 24, 24);
                    card.Dots[k].rectTransform.Center(100 - 67 + k * 15, 180 + 116);
                }
                card.Branch = SkinUi.Image("Branch", card.Board.transform, courseBranch, 40, 40);
                card.Branch.rectTransform.Center(100, 180 + 144);
                // main_diff_board text_course_title, centred 49 px below the card origin.
                card.Name = SkinUi.Text("CourseName", card.Board.transform, font, outlineMaterial, 34, new Color32(0, 0, 0, 255), 0.25f);
                card.Name.rectTransform.Center(100, 180 + 49);
            }
            uraChange = SkinUi.Image("UraChange", coursePanel, null, 340, 400);
            uraChange.rectTransform.TopLeft(CourseSlotX[3] - 170, CourseBoardY - 210);
            uraChange.enabled = false;
            header = SkinUi.Text("Title", coursePanel, font, outlineMaterial, 48, new Color32(0, 0, 0, 255), 0.25f);
            header.rectTransform.Center(960, 178);
            headerSub = SkinUi.Text("Subtitle", coursePanel, font, outlineMaterial, 30, new Color32(0, 0, 0, 255), 0.25f);
            headerSub.rectTransform.Center(960, 242);
            balloon = SkinUi.Image("PlayerBalloon", coursePanel, playerBalloon, 124, 124);
            optionPanel = new OptionPanel(coursePanel, optionArt, font, outlineMaterial);
            optionPanel.RowTapped += OnOptionRowTapped;
            optionPanel.OutsideTapped += CloseOptions;
        }

        void AddClick(Image image, Difficulty difficulty)
        {
            image.raycastTarget = true;
            image.gameObject.AddComponent<PointerRelay>().Clicked = () =>
            {
                if (!AcceptsInput() || Phase != State.CourseSelect) return;
                var target = difficulty == Difficulty.Oni && Cursor.IsUra ? Difficulty.Ura : difficulty;
                if (target >= Difficulty.Easy && wheelBoards[Focused].Info.Course(target) == null) return;
                // Walk the cursor so a click obeys the same rules as the drum.
                for (int guard = 0; guard < 8 && Cursor.Selected != target; guard++)
                {
                    if (Order(Cursor.Selected) < Order(target)) Cursor.Right(); else Cursor.Left();
                }
                if (Cursor.Selected == target) Confirm();
            };
            static int Order(Difficulty d) => d == Difficulty.Ura ? (int)Difficulty.Oni : (int)d;
        }

        void OnBoardClicked(Board board)
        {
            if (Phase != State.Browsing || !AcceptsInput()) return;
            int index = wheelBoards.IndexOf(board);
            if (index == Focused) { Confirm(); return; }
            sfx.PlayOneShot(ka);
            int delta = index - Focused;
            if (delta > songs.Length / 2) delta -= songs.Length;
            else if (delta < -songs.Length / 2) delta += songs.Length;
            Navigate(delta);
        }
    }

    public sealed class PointerRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
