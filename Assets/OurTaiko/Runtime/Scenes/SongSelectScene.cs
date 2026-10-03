using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
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

        [Header("Stage")]
        public RectTransform wheel, coursePanel;
        public SongSelectView view;
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

        [Header("Global chrome")]
        public ArcadeOverlayArt overlay;

        [Header("Timelines")]
        public TextAsset songBoardTimeline, cursorGlowTimeline, uraLoopTimeline;

        [Header("Audio")]
        public AudioSource bgm, preview, sfx, voice;
        public AudioClip don, ka, uraSwitch, voiceEnter, voiceStartSong;

        const double MoveMs = 166, OpenHoldMs = 61 / 120.0 * 1000, OpenGrowMs = 233, CloseMs = 13 / 0.06;
        const double CourseExitMoveMs = 500, CourseEnterMoveMs = 800, BoardFadeMs = 166;
        const double CourseFadeDelayMs = 400, CourseFadeMs = 483, GenreFadeMs = 200, BackgroundLoopMs = 15000;
        const double BgmResumeMs = 330, UraChangeMs = 1500, UraSwapMs = 450;
        const int UraCells = 90;
        static readonly string[] ChipNames = { "かんたん", "ふつう", "むずかしい", "おに", "おに(裏)" };

        public State Phase { get; private set; } = State.Browsing;
        public const int ListTimerSeconds = 100, CourseTimerSeconds = 60;
        public ArcadeTimerView TimerView { get; private set; }
        public CoinOverlayView Coins { get; private set; }
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
            public SongBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CrownPosition, CrownSize, RootOffset;
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
        Vector2 framePosition, glowPosition, balloonPosition;
        Vector2[] backgroundPositions;

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
            if (view == null) throw new InvalidOperationException("SongSelect requires a saved layout. Run OurTaiko/Apply Song Select Layout in the Editor.");
            wheel.gameObject.SetActive(true);
            BindBoards();
            backgroundPositions = backgroundTiles.Select(tile => tile.rectTransform.anchoredPosition).ToArray();
            int remembered = Array.IndexOf(songs, switcher.SelectedSong);
            Focused = remembered >= 0 ? remembered : 0;
            currentGenre = previousGenre = FocusedSong.genre;
            BindCoursePanel();
            TimerView = new ArcadeTimerView(view.overlays, overlay);
            Coins = new CoinOverlayView(view.overlays, overlay);
            // Slice the 90-cell Oni/Ura change sheets up front so the first flip does not hitch.
            UraFrames(ref uraToUraCells, uraChangeToUra);
            UraFrames(ref uraToOniCells, uraChangeToOni);
            SetPositions(true, 0);
            // SecondLoading: the initially focused board opens at once, without the 508 ms hold.
            OpenFocused(holdMs: 0);
            coursePanel.gameObject.SetActive(false);
            DrawOverlays(0);
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
            DrawOverlays(now);
        }

        void HandleInput()
        {
            if (switcher.IsInputBlocked) return;
            // song_select.cpp: the back key leaves for Entry from any state (an open option panel closes first).
            if (InputManager.GetKeyDown(InputKey.Back))
            {
                if (IsOptionPanelOpen) CloseOptions();
                else switcher.SwitchScene(SceneSwitcher.EntryScene);
                return;
            }
            bool leftKa = InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft);
            bool rightKa = InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight);
            bool donHit = InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm);
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
                double position = view.wheelCentre.y + offset * view.rowPitch + Math.Sign(offset) * view.expandGap;
                double cross = view.wheelCentre.x + offset * view.rowCurve;
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
                MoveBoard(other, other.Position < view.wheelCentre.y ? -150 : 1080 + 150, other.Cross, CourseEnterMoveMs);
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
                backgroundTiles[i].rectTransform.anchoredPosition = backgroundPositions[i] + Vector2.left * move;
                backgroundTiles[i + 2].rectTransform.anchoredPosition = backgroundPositions[i + 2] + Vector2.left * move;
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
            board.Root.anchoredPosition = new Vector2((float)board.Cross, -(float)board.Position) + board.RootOffset;

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
            var expansion = Vector2.up * (float)(board.View.expansionHeight * pb);
            board.Panel.rectTransform.sizeDelta = board.PanelSize + expansion;
            board.Glow.rectTransform.sizeDelta = board.GlowSize + expansion;
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            board.Glow.Alpha((float)(pb * pulse));
            board.Title.rectTransform.anchoredPosition = board.TitlePosition + board.View.titleOpenOffset * (float)pb;
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
            board.Crown.rectTransform.sizeDelta = board.CrownSize * (0.75f + 0.25f * p);
            board.Crown.rectTransform.anchoredPosition = board.CrownPosition + board.View.crownOpenOffset * p;
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
            float x = column >= 0 ? cards[column].Board.rectTransform.anchoredPosition.x
                : (selected == Difficulty.Modifier ? option : back).rectTransform.anchoredPosition.x;
            bool hideForUra = column == 3 && ura >= 0;
            frame.enabled = column >= 0 && !hideForUra;
            glow.enabled = column < 0;
            balloon.enabled = !hideForUra;
            float cardX = cards[0].Board.rectTransform.anchoredPosition.x;
            frame.rectTransform.anchoredPosition = framePosition + Vector2.right * (x - cardX);
            glow.rectTransform.anchoredPosition = glowPosition + Vector2.right * (x - back.rectTransform.anchoredPosition.x);
            balloon.rectTransform.anchoredPosition = balloonPosition + Vector2.right * (x - cardX);
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

        // ---------------------------------------------------------------- saved view binding

        void BindBoards()
        {
            var saved = view.songBoards ?? Array.Empty<SongBoardView>();
            for (int i = 0; i < songs.Length; i++)
            {
                // The authored song list reuses its scene objects. New songs use the same editable prefab.
                var item = i < saved.Length ? saved[i] : Instantiate(view.boardPrefab, wheel);
                item.gameObject.SetActive(true);
                var board = new Board
                {
                    View = item, Song = songs[i], Info = songs[i].ReadInfo(), Root = item.Root,
                    Group = item.group, Glow = item.glow, Panel = item.panel, Crown = item.crown,
                    Title = item.title, Subtitle = item.subtitle, Contents = item.contents,
                    PanelSize = item.panel.rectTransform.sizeDelta, GlowSize = item.glow.rectTransform.sizeDelta,
                    TitlePosition = item.title.rectTransform.anchoredPosition,
                    CrownPosition = item.crown.rectTransform.anchoredPosition, CrownSize = item.crown.rectTransform.sizeDelta
                };
                if (i < saved.Length)
                    board.RootOffset = item.Root.anchoredPosition - item.authoredWheelPosition;
                item.click.Clicked = () => OnBoardClicked(board);
                board.Panel.sprite = boards[songs[i].genre];
                board.Title.text = board.Info.Title;
                board.Title.Squeeze(860);
                board.Subtitle.text = board.Info.Subtitle;
                board.Subtitle.Squeeze(860, 0.75f);
                foreach (var plate in item.plates)
                {
                    var info = board.Info.Course(plate.difficulty);
                    plate.group.gameObject.SetActive(info != null);
                    if (info == null) continue;
                    ((RectTransform)plate.group.transform).anchoredPosition += Vector2.right *
                        (PlatePosition(board.Info, plate.difficulty, item.platePitch) - plate.authoredContentX);
                    plate.level.sprite = levels[(int)plate.difficulty * 11 + Mathf.Clamp(info.Level, 1, 11) - 1];
                    plate.branch.enabled = info.IsBranching;
                    board.Plates.Add(new Plate { Difficulty = plate.difficulty, Group = plate.group });
                }
                wheelBoards.Add(board);
            }
            for (int i = songs.Length; i < saved.Length; i++) saved[i].gameObject.SetActive(false);
        }

        static float PlatePosition(SongInfo info, Difficulty difficulty, float pitch)
        {
            var columns = new List<Difficulty>();
            for (var d = Difficulty.Easy; d <= Difficulty.Hard; d++)
                if (info.Has(d)) columns.Add(d);
            if (info.Has(Difficulty.Oni) || info.Has(Difficulty.Ura)) columns.Add(Difficulty.Oni);
            int index = columns.IndexOf(difficulty == Difficulty.Ura ? Difficulty.Oni : difficulty);
            return (index - (columns.Count - 1) / 2f) * pitch;
        }

        void BindCoursePanel()
        {
            mark = view.mark; backboard = view.backboard; back = view.back; option = view.option;
            auto = view.auto; frame = view.frame; glow = view.glow; balloon = view.balloon; uraChange = view.uraChange;
            header = view.header; headerSub = view.headerSub;
            framePosition = frame.rectTransform.anchoredPosition;
            glowPosition = glow.rectTransform.anchoredPosition;
            balloonPosition = balloon.rectTransform.anchoredPosition;
            AddClick(back, Difficulty.Back);
            AddClick(option, Difficulty.Modifier);
            for (int i = 0; i < cards.Length; i++)
            {
                var saved = view.cards[i];
                cards[i] = new CourseCard { Board = saved.board, Crown = saved.crown, Star = saved.star,
                    Level = saved.level, Bar = saved.bar, Branch = saved.branch, Dots = saved.dots, Name = saved.name };
                AddClick(saved.board, (Difficulty)i);
            }
            optionPanel = new OptionPanel(view.options, optionArt);
            optionPanel.RowTapped += OnOptionRowTapped;
            optionPanel.OutsideTapped += CloseOptions;
        }

        void DrawOverlays(double now)
        {
            if (TimerView == null) return;
            TimerView.Show(Phase == State.Browsing ? ListTimerSeconds : CourseTimerSeconds);
            // coin_overlay: the invite shows while a 2P join would still be allowed (songs played < 2).
            Coins.ShowInvite(switcher.SongsPlayed < 2, now);
        }

        void AddClick(Image image, Difficulty difficulty)
        {
            image.raycastTarget = true;
            image.GetComponent<PointerRelay>().Clicked = () =>
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

}
