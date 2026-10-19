using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using State = OurTaiko.SongSelectManager.State;
using ItemKind = OurTaiko.SongSelectManager.ItemKind;
using WheelItem = OurTaiko.SongSelectManager.WheelItem;

namespace OurTaiko
{
    // Nijiiro single-player song select (scenes/song_select.cpp + Scripts/song_select/song_select.lua):
    // the vertical song-board wheel, the course-select panel and the 演奏オプション panel behind the option
    // button. ServerLogin's categories follow the local songs as closed genre folders; a folder opens
    // inline (Navigator::load_current_directory without child folders): its board turns into もどる,
    // its songs follow with a もどる every ten songs, and opening another folder closes it first.
    // A root もどる ends the list (the wheel wraps, so it sits above the first song) and leaves for
    // Entry like the back key.
    // Nested folders, the standalone neiro panel, dan boards and 2P are not ported.
    //
    // This is the view: SongSelectManager holds the wheel, focus, phase, cursor and options and runs
    // every command; this component draws it from the saved layout, animates its events (board
    // slides, select_on/off, genre fades, the course panel, the Ura flip, the option panel) and plays
    // the sounds, voices, BGM and preview.
    public sealed partial class SongSelectScene : MonoBehaviour
    {
        [Header("Stage")]
        public RectTransform wheel, coursePanel;
        public SongSelectView view;
        public Image[] backgroundTiles; // previous genre x2, current genre x2

        [Header("Wheel art")]
        public Sprite[] genreBackgrounds, boards;
        public Sprite cursorGlow, branch;
        public Sprite[] plates, levels, stars, crownClear, crownFullCombo, crownDonderful;

        [Header("Folder art")]
        [Tooltip("folder_graphic 0..12 (open folder board), indexed like boards.")]
        public Sprite[] folderBoards;
        [Tooltip("box_chara 0..9, left and right 960x480 halves.")]
        public Sprite[] charaLeft, charaRight;
        public Sprite backBoard;

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
        public TextAsset songBoardTimeline, cursorGlowTimeline, uraLoopTimeline, folderBoardTimeline;

        [Header("Audio")]
        public AudioSource bgm, preview, sfx, voice;
        public AudioClip don, ka, uraSwitch, voiceEnter, voiceStartSong;

        const double MoveMs = 166, OpenHoldMs = 61 / 120.0 * 1000, OpenGrowMs = 233, CloseMs = 13 / 0.06, FolderCloseMs = 8 / 0.06;
        const double CourseExitMoveMs = 500, CourseEnterMoveMs = 800, BoardFadeMs = 166;
        const double CourseFadeDelayMs = 400, CourseFadeMs = 483, GenreFadeMs = 200, BackgroundLoopMs = 15000;
        const double UraChangeMs = 1500, UraSwapMs = 450;
        const int UraCells = 90;
        static readonly string[] ChipNames = { "かんたん", "ふつう", "むずかしい", "おに", "おに(裏)" };

        public const int ListTimerSeconds = 100, CourseTimerSeconds = 60;
        public SongSelectManager Manager { get; private set; }
        public ArcadeTimerView TimerView { get; private set; }
        public CoinOverlayView Coins { get; private set; }
        public double CourseFade => Manager.Phase == State.Browsing ? 0 : Clamp01((Now - courseEnteredAt - CourseFadeDelayMs) / CourseFadeMs);
        public bool IsPreviewPlaying => preview != null && preview.IsAudioPlaying();

        sealed class Plate { public Difficulty Difficulty; public CanvasGroup Group; }
        // A board view and its authored sizes/positions before any song changed them.
        sealed class Slot
        {
            public SongBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CrownPosition, CrownSize, RankPosition, RootOffset;
            public Vector2[] PlateBase;
            public bool Saved;
        }
        // Only boards on screen hold a view (Slot): the authored songs keep their saved scene
        // boards, the rest share pooled SongBoard prefab instances, so a long online catalog costs
        // a dozen board objects instead of one per song.
        sealed class FolderSlot
        {
            public FolderBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CountPosition;
        }
        // How one wheel item is drawn: its bound view and its slide, select_on/off and fade timing.
        sealed class Board
        {
            public WheelItem Item;
            public ItemKind Kind => Item.Kind;
            public int Folder => Item.Folder;
            public int Genre => Item.Genre;
            public SongDefinition Song => Item.Song;
            public SongInfo Info => Item.Info;
            public FolderSlot FolderSlot;
            public Slot Slot;
            public SongBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CrownPosition, CrownSize, RankPosition, RootOffset;
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
            public ScoreRankView Rank;
            public Image[] Dots;
            public TextMeshProUGUI Name;
        }

        readonly Dictionary<WheelItem, Board> visuals = new Dictionary<WheelItem, Board>();
        readonly Stack<Slot> boardPool = new Stack<Slot>();
        readonly Stack<FolderSlot> folderPool = new Stack<FolderSlot>();
        LumenClip folderClip;
        bool restackBoards;
        readonly CourseCard[] cards = new CourseCard[4];
        LumenClip songBoard, glowClip, uraLoop;
        SceneSwitcher switcher;
        double startedAt, genreChangedAt = -10000, courseEnteredAt, uraChangedAt = -1;
        int previousGenre, currentGenre;
        bool uraChangeToUraSide, started;
        Sprite[] uraToUraCells, uraToOniCells;
        Image mark, backboard, back, option, auto, frame, glow, balloon, uraChange;
        TextMeshProUGUI header, headerSub;
        OptionPanel optionPanel;
        Vector2 framePosition, glowPosition, balloonPosition;
        Vector2[] backgroundPositions;
        Func<bool> courseReady;

        double Now => GameTimeline.FrameTime * 1000 - startedAt;
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        Board BoardOf(WheelItem item) => visuals[item];
        Board FocusedBoard => visuals[Manager.FocusedItem];

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            startedAt = GameTimeline.FrameTime * 1000;
            songBoard = LumenClip.Parse(songBoardTimeline.text);
            glowClip = LumenClip.Parse(cursorGlowTimeline.text);
            uraLoop = LumenClip.Parse(uraLoopTimeline.text);
            folderClip = folderBoardTimeline != null ? LumenClip.Parse(folderBoardTimeline.text) : LumenClip.Empty;
            switcher.SceneChanging += OnSceneChanging;
            if (view == null) throw new InvalidOperationException("SongSelect requires a saved layout. Run OurTaiko/Apply Song Select Layout in the Editor.");
            wheel.gameObject.SetActive(true);
            Manager = SongSelectManager.EnsureInstance();
            Manager.Load(optionArt.hitSounds != null ? optionArt.hitSounds.Count : 0);
            Manager.CourseReady = courseReady = () => CourseFade >= 1;
            Manager.SoundRequested += OnSound;
            Manager.VoiceRequested += OnVoice;
            Manager.FolderChanged += OnFolderChanged;
            Manager.WheelRebuilt += OnWheelRebuilt;
            Manager.SearchRequested += OnSearchRequested;
            Manager.FocusChanged += OnFocusChanged;
            Manager.PhaseChanged += OnPhaseChanged;
            Manager.UraToggled += OnUraToggled;
            Manager.OptionsOpened += OnOptionsOpened;
            Manager.OptionChanged += OnOptionChanged;
            Manager.OptionsClosing += OnOptionsClosing;
            BindBoards();
            BindSwipe();
            backgroundPositions = backgroundTiles.Select(tile => tile.rectTransform.anchoredPosition).ToArray();
            currentGenre = previousGenre = Manager.FocusedItem.Genre;
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
            if (view.search != null) view.search.Bind(Manager);
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, ka, uraSwitch);
            if (optionArt.hitSounds != null) { sfx.PrepareAudioEffects(optionArt.hitSounds.don); sfx.PrepareAudioEffects(optionArt.hitSounds.ka); }
            voice.PrepareAudioTracks(voiceEnter, voiceStartSong, optionArt.voice);
            bgm.loop = true;
            bgm.PlayAudio();
            PlayVoice(voiceEnter);
            started = true;
        }

        void OnDisable() => StopPreview();

        void OnDestroy()
        {
            StopPreview();
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
            if (Manager == null) return;
            Manager.SoundRequested -= OnSound;
            Manager.VoiceRequested -= OnVoice;
            Manager.FolderChanged -= OnFolderChanged;
            Manager.WheelRebuilt -= OnWheelRebuilt;
            Manager.SearchRequested -= OnSearchRequested;
            Manager.FocusChanged -= OnFocusChanged;
            Manager.PhaseChanged -= OnPhaseChanged;
            Manager.UraToggled -= OnUraToggled;
            Manager.OptionsOpened -= OnOptionsOpened;
            Manager.OptionChanged -= OnOptionChanged;
            Manager.OptionsClosing -= OnOptionsClosing;
            // A newer song select may already have bound itself.
            if (Manager.CourseReady == courseReady) Manager.CourseReady = null;
        }

        void OnSceneChanging(string scene)
        {
            StopPreview();
            bgm.StopAudio(); preview.StopAudio();
            Manager.Leave();
        }

        void Update()
        {
            if (!started) return;
            double now = Now;
            if (view.search != null) view.search.Tick();
            if (view.search == null || !view.search.BlocksInput) Manager.HandleInput();
            if (Manager.Phase == State.Decided && !voice.IsAudioPlaying() && !switcher.IsSwitching) Manager.StartSong();
            StepSwipe(now);
            UpdatePreview(now);
            DrawBackground(now);
            var items = Manager.Items;
            for (int i = 0; i < items.Count; i++) DrawBoard(BoardOf(items[i]), i == Manager.Focused, now);
            if (restackBoards) RestackBoards();
            if (Manager.Phase != State.Browsing) DrawCoursePanel(now);
            if (optionPanel.Draw(now)) Manager.OptionsHidden();
            DrawOverlays(now);
            if (view.bestScore != null)
                view.bestScore.Show(Manager.FocusedSong, Manager.FocusedKind == ItemKind.Song ? Manager.FocusedItem.Info : null,
                    Manager.IsOptionPanelOpen ? 0 : 1, GameTimeline.FrameTime);
        }

        // ---------------------------------------------------------------- manager events

        void OnSound(SongSelectManager.Sound sound)
            => sfx.PlayAudioOneShot(sound == SongSelectManager.Sound.Don ? don : sound == SongSelectManager.Sound.Ka ? ka : uraSwitch);

        void OnVoice(SongSelectManager.Voice clip)
            => PlayVoice(clip == SongSelectManager.Voice.StartSong ? voiceStartSong : optionArt.voice);

        void PlayVoice(AudioClip clip)
        {
            voice.StopAudio();
            voice.clip = clip;
            if (clip != null) voice.PlayAudio();
        }

        // Boards leaving the wheel release their views; new ones start on the anchor's slot, songs
        // fading in as they slide to their rows (もどる and the folder board appear at once).
        void OnFolderChanged(SongSelectManager.FolderChange change)
        {
            CancelSwipe();
            var anchor = BoardOf(change.Anchor);
            foreach (var item in change.Removed)
            {
                ReleaseView(BoardOf(item));
                visuals.Remove(item);
            }
            foreach (var item in change.Added)
            {
                double fadeFrom = item.Kind == ItemKind.Song ? 0 : 1;
                visuals[item] = new Board
                {
                    Item = item, Position = anchor.Position, Cross = anchor.Cross,
                    FadeFrom = fadeFrom, FadeTo = 1, FadeStart = fadeFrom < 1 ? Now : -1,
                };
            }
        }

        void OnSearchRequested() => view.search?.Open();

        void OnWheelRebuilt()
        {
            CancelSwipe();
            StopPreview();
            foreach (var board in visuals.Values) ReleaseView(board);
            visuals.Clear();
            foreach (var item in Manager.Items) visuals[item] = new Board { Item = item };
            SetPositions(true, 0); OpenFocused(0); ChangeGenre(Manager.FocusedItem.Genre);
            if (view.search != null) view.search.RefreshSummary();
        }

        void OnFocusChanged(WheelItem previous, SongSelectManager.FocusReason reason)
        {
            // A swipe walking the focus lays the boards out itself and opens the board it rests on.
            if (swipeStepping)
            {
                ChangeGenre(Manager.FocusedItem.Genre);
                return;
            }
            CancelSwipe();
            if (reason == SongSelectManager.FocusReason.Navigate && visuals.TryGetValue(previous, out var left) && left.OpenStart >= 0)
            {
                left.CloseStart = Now; left.OpenStart = -1;
            }
            SetPositions(false, MoveMs);
            // A folder enter or exit snaps the new focus open (no 508 ms hold).
            OpenFocused(reason == SongSelectManager.FocusReason.Navigate ? OpenHoldMs : 0);
            if (reason != SongSelectManager.FocusReason.FolderClosed) ChangeGenre(Manager.FocusedItem.Genre);
            StopPreview();
        }

        void OnPhaseChanged(State previous)
        {
            CancelSwipe();
            if (Manager.Phase == State.CourseSelect) EnterCourseSelect();
            else if (Manager.Phase == State.Browsing && previous != State.Browsing) ExitCourseSelect();
        }

        void OnUraToggled()
        {
            uraChangedAt = Now; uraChangeToUraSide = Manager.Cursor.IsUra;
        }

        void OnOptionsOpened(OptionMenu menu) => optionPanel.Open(menu, Now);

        void OnOptionChanged(int direction)
        {
            optionPanel.Changed(direction, Now);
            // step_neiro previews the new set's don; 無音 plays nothing.
            var menu = Manager.OptionMenu;
            if (menu.Current == OptionRow.Neiro && optionArt.hitSounds != null
                && optionArt.hitSounds.TryGet(menu.Options.neiro, out var neiro, out _))
                sfx.PlayAudioOneShot(neiro, AudioGroup.Drum);
        }

        void OnOptionsClosing() => optionPanel.Close(Now);

        // ---------------------------------------------------------------- wheel animation

        void ChangeGenre(int genre)
        {
            previousGenre = currentGenre; currentGenre = genre; genreChangedAt = Now;
        }

        void OpenFocused(double holdMs)
        {
            var board = FocusedBoard;
            board.OpenStart = Now; board.Hold = holdMs; board.CloseStart = -1;
        }

        // Navigator::set_positions (vertical gallery): pitch 135, +-120 around the open board,
        // each row 40 px further right; a jump of a whole screen snaps instead of sliding.
        void SetPositions(bool snap, double duration)
        {
            var items = Manager.Items;
            int count = items.Count, focused = Manager.Focused;
            for (int i = 0; i < count; i++)
            {
                double offset = i - focused;
                if (offset > count / 2.0) offset -= count;
                else if (offset < -count / 2.0) offset += count;
                double position = view.wheelCentre.y + offset * view.rowPitch + Math.Sign(offset) * view.expandGap;
                double cross = view.wheelCentre.x + offset * view.rowCurve;
                var board = BoardOf(items[i]);
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
            var board = FocusedBoard;
            courseEnteredAt = Now;
            uraChangedAt = -1;
            // Navigator::enter_diff_select: the other on-screen boards leave by 150 px and fade out.
            var items = Manager.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var other = BoardOf(items[i]);
                if (i == Manager.Focused || other.Position < -100 || other.Position > 1180) continue;
                MoveBoard(other, other.Position < view.wheelCentre.y ? -150 : 1080 + 150, other.Cross, CourseEnterMoveMs);
                FadeBoard(other, 0);
            }
            FillCoursePanel(board);
            coursePanel.gameObject.SetActive(true);
        }

        void ExitCourseSelect()
        {
            coursePanel.gameObject.SetActive(false);
            SetPositions(false, CourseExitMoveMs);
            foreach (var board in visuals.Values) FadeBoard(board, 1);
            // Course back replays select_on immediately on the focused board.
            OpenFocused(holdMs: 0);
        }

        // ---------------------------------------------------------------- preview / bgm

        SongPreview songPreview;
        SongPreview Preview => songPreview ??= new SongPreview(this, preview);

        void UpdatePreview(double now)
        {
            var board = FocusedBoard;
            bool open = Manager.Phase == State.Browsing && board.Song != null && board.OpenStart >= 0 && now - board.OpenStart >= board.Hold + OpenGrowMs;
            bool remote = board.Song != null && Online.OnlineManager.Instance?.IsOnline(board.Song) == true;
            if (open && !Preview.Started && (remote || board.Song.music != null || !string.IsNullOrEmpty(board.Song.audioPath)))
                Preview.Play(board.Song, board.Info.DemoStart);
        }

        void StopPreview() => Preview.Stop();
    }
}
