using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro single-player Entry (scenes/entry.cpp + Scripts/entry/*.lua, entry_credit_arcade):
    // the credit screen waits for a drum face hit, 1P joins (nameplate and control guide fade in),
    // and once the join animation would have finished the 演奏ゲーム board opens; a face hit (or the
    // 60 s timer) picks it and the scene moves to SongSelect. Not ported: the 3D Don and its join
    // cloud, 2P joining, the other boards (特訓モード / きせかえ / ゲーム設定), the costume menu and the
    // ALL.Net indicator. The screen is built in Awake.
    public sealed class EntryScene : MonoBehaviour
    {
        public const int TimerSeconds = 60;

        public TMP_FontAsset font;
        public Material outlineMaterial;
        public RectTransform stage;

        [Header("Background")]
        public Sprite background;
        public Sprite streetLit;
        public Sprite[] glow, twinkle;

        [Header("Credit and mode select")]
        public Sprite creditPill;
        public Sprite creditFlash, boardOn, boardOff, boardFlash, boardCursor;

        [Header("Global chrome")]
        public ArcadeOverlayArt overlay;
        public NameplateView nameplatePrefab;

        [Header("Timelines")]
        public TextAsset backgroundTimeline;
        public TextAsset creditRowTimeline, creditFadeTimeline, modeBoardTimeline, cursorGlowTimeline;

        [Header("Audio")]
        public AudioSource bgm;
        public AudioSource sfx, voice, timerVoice;
        public AudioClip don, ka, cloud, entryStart, selectMode, timerBlip, timerVoice30, timerVoice10, timerVoice5;

        public EntryFlow Flow { get; private set; }
        public ArcadeTimer Timer { get; private set; }
        public EntryCredit Credit { get; private set; }
        public EntryModeBoard Board { get; private set; }
        public ControlGuideView Guide { get; private set; }
        public CoinOverlayView Coins { get; private set; }
        public NameplateView Nameplate { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;
        EntryBackground backdrop;
        ArcadeTimerView timerView;
        CanvasGroup nameplateGroup;
        bool announced, creditGone;
        double modeShownAt = double.NaN;

        public double Now => Time.realtimeSinceStartupAsDouble * 1000;

        static LumenClip Clip(TextAsset asset) => asset != null ? LumenClip.Parse(asset.text) : LumenClip.Empty;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            double now = Now;
            Flow = new EntryFlow(now);
            Timer = new ArcadeTimer(TimerSeconds, now);
            Build();
            switcher.SceneChanging += OnSceneChanging;
            Show(now);
        }

        void Start()
        {
            bgm.loop = true;
            bgm.Play();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            bgm.Stop(); voice.Stop(); timerVoice.Stop();
        }

        void Build()
        {
            backdrop = new EntryBackground(stage, background, streetLit, glow, twinkle, Clip(backgroundTimeline));
            Board = new EntryModeBoard(stage, boardOn, boardOff, boardFlash, boardCursor, font, outlineMaterial,
                Clip(modeBoardTimeline), Clip(cursorGlowTimeline));
            Credit = new EntryCredit(stage, creditPill, creditFlash, font, outlineMaterial, Clip(creditRowTimeline), Clip(creditFadeTimeline));
            Guide = new ControlGuideView(stage, overlay);
            if (nameplatePrefab != null)
            {
                Nameplate = Instantiate(nameplatePrefab, stage);
                Nameplate.name = "Nameplate";
                // nameplate_entry_left
                Nameplate.Place(14, 910);
                nameplateGroup = Nameplate.gameObject.AddComponent<CanvasGroup>();
                nameplateGroup.alpha = 0;
            }
            timerView = new ArcadeTimerView(stage, overlay);
            StatusChips.Build(stage, overlay);
            Coins = new CoinOverlayView(stage, overlay, font, outlineMaterial);
            var touch = SkinUi.Image("TouchArea", stage, null, 1920, 1080);
            touch.rectTransform.TopLeft(0, 0);
            touch.color = Color.clear;
            touch.raycastTarget = true;
            touch.gameObject.AddComponent<PointerRelay>().Clicked = Don;
        }

        void Update()
        {
            double now = Now;
            if (!switcher.IsInputBlocked && !HasLeft)
            {
                if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Don();
                else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.RightKa)
                    || InputManager.GetKeyDown(InputKey.MenuLeft) || InputManager.GetKeyDown(InputKey.MenuRight)) Ka();
            }
            // The arcade timer stands still on the credit screen and stops once a board is picked.
            if (Flow.State == EntryFlow.Phase.SelectMode && !Flow.IsFinished(now)) TickTimer(now);
            if (!announced && Flow.IsModeReady(now) && !voice.isPlaying)
            {
                announced = true;
                Play(voice, selectMode);
            }
            if (Flow.IsFinished(now) && !HasLeft)
            {
                HasLeft = true;
                switcher.SwitchScene(SceneSwitcher.SongSelectScene);
            }
            Show(now);
        }

        // A drum face hit: join on the credit screen, decide on the board.
        public void Don()
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            double now = Now;
            if (Flow.State == EntryFlow.Phase.SelectSide)
            {
                if (!Flow.Join(now)) return;
                Play(sfx, cloud, oneShot: true);
                Play(voice, entryStart);
                Play(sfx, don, oneShot: true);
                return;
            }
            if (!Flow.IsModeReady(now) || Flow.IsSelected) return;
            Play(sfx, don, oneShot: true);
            Flow.Select(now);
        }

        // A rim hit: the single-board list has nowhere to move, but the board still answers.
        public void Ka()
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            double now = Now;
            if (Flow.State != EntryFlow.Phase.SelectMode || !Flow.IsModeReady(now) || Flow.IsSelected) return;
            Play(sfx, ka, oneShot: true);
        }

        void TickTimer(double now)
        {
            var cue = Timer.Update(now);
            if ((cue & ArcadeTimer.Cue.Blip) != 0) Play(sfx, timerBlip, oneShot: true);
            if ((cue & ArcadeTimer.Cue.Voice30) != 0) Play(timerVoice, timerVoice30);
            if ((cue & ArcadeTimer.Cue.Voice10) != 0) Play(timerVoice, timerVoice10);
            if ((cue & ArcadeTimer.Cue.Voice5) != 0) Play(timerVoice, timerVoice5);
            if ((cue & ArcadeTimer.Cue.Finished) != 0)
            {
                // Timer.lua: at 0 every voice stops and the current board is picked.
                timerVoice.Stop();
                Flow.Select(now);
            }
        }

        static void Play(AudioSource source, AudioClip clip, bool oneShot = false)
        {
            if (source == null || clip == null) return;
            if (oneShot) { source.PlayOneShot(clip); return; }
            source.Stop();
            source.clip = clip;
            source.Play();
        }

        void Show(double now)
        {
            backdrop.Show(now - Flow.StartedAt);
            bool modeReady = Flow.IsModeReady(now);
            if (modeReady && double.IsNaN(modeShownAt)) modeShownAt = now;
            Board.Root.gameObject.SetActive(modeReady);
            if (modeReady)
                Board.Show(now, now - modeShownAt, Flow.BoardFade(now), Flow.SelectedAt.HasValue ? now - Flow.SelectedAt.Value : -1);

            if (Flow.State == EntryFlow.Phase.SelectSide) Credit.ShowWaiting(now - Flow.StartedAt, now - Flow.StartedAt);
            else if (!creditGone) creditGone = !Credit.ShowDecided(now - Flow.JoinedAt.Value, 0);

            // The guide runs from the scene start over the credit rows, then restarts with the
            // player's own indicator, fading in with the nameplate.
            float plate = Flow.NameplateAlpha(now);
            if (Flow.JoinedAt.HasValue) Guide.Show(now - Flow.JoinedAt.Value, plate);
            else Guide.Show(now - Flow.StartedAt, 1);
            if (nameplateGroup != null) nameplateGroup.alpha = plate;

            timerView.Show(Timer, now);
            // coin_overlay: the 2P invite appears once the credit rows are gone and only 1P is in.
            Coins.ShowInvite(Flow.JoinedAt.HasValue && creditGone, now - Flow.StartedAt);
        }
    }
}
