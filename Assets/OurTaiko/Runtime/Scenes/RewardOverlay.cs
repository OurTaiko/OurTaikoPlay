using System;
using System.Collections.Generic;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Online;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko
{
    // What the players were given, shown over the screen they are on (RewardInbox holds it). It
    // waits for a screen where nothing runs against the clock (Shows): never in a play, a loading
    // or the online room, and not while a scene changes or the settings are open. On such a screen
    // it also waits for the screen's own show to be over (ISettledScreen: a result's scores and
    // crown, a menu's opening) and to stay over for QuietSeconds; then it dims the screen and shows
    // the rewards one at a time:
    //
    //   0.0 s  the screen dims
    //   0.2 s  the 獲得 banner drops in, past its place and back
    //   0.55 s a title's band slides in from the right, past its place and back (a 段位 emblem
    //          spins up from small instead); a light runs across the band at 1.15 s
    //   1.1 s  what gave it fades in under it
    //   1.6 s  "don: next" (or "close" on the last), blinking
    //
    // Don (either side, either seat) or a tap before 1.6 s shows everything at once; after it, it
    // goes to the next reward, or closes and marks them seen. While it is up the scene under it
    // takes no keys (ModalLayers) and no taps (the shade).
    //
    // Each kind of reward is drawn by its entry in Draw; a kind it does not know is marked seen
    // without being shown.
    [DefaultExecutionOrder(-100)]
    public sealed class RewardOverlay : MonoBehaviour
    {
        public const double DimSeconds = 0.2, BannerAt = 0.2, BannerSeconds = 0.45, ItemAt = 0.55, ItemSeconds = 0.5,
            ShineAt = 1.15, ShineSeconds = 0.7, FromAt = 1.1, FromSeconds = 0.3, ReadyAt = 1.6, QuietSeconds = 0.5;
        const float Scale = 3, ShadeAlpha = 0.82f;

        // The screens it shows on: menus and results, where nothing runs on a clock.
        public static readonly HashSet<string> Shows = new HashSet<string>
        {
            SceneSwitcher.EntryScene, SceneSwitcher.SongSelectScene, SceneSwitcher.ResultScene, SceneSwitcher.DanSelectScene,
            SceneSwitcher.DanResultScene, SceneSwitcher.GhostSelectScene,
        };

        public static RewardOverlay Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null) return;
            var root = new GameObject(nameof(RewardOverlay), typeof(RectTransform));
            DontDestroyOnLoad(root);
            root.AddComponent<RewardOverlay>();
        }

        public bool IsShowing => batch.Count > 0;
        public Reward Current => IsShowing ? batch[index] : null;
        public int Index => index;
        public int Count => batch.Count;
        // The texts on screen, for tests.
        public string AllText => content == null ? "" : string.Join("\n", content.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text));
        // The reward on screen has finished coming in.
        public bool IsReady => IsShowing && Elapsed >= ReadyAt;

        RewardInbox inbox;
        readonly List<Reward> batch = new List<Reward>();
        int index;
        double shownAt;
        // Since when the screen has been calm (MayShow); NaN while it is not.
        float calmSince = float.NaN;
        // The active scene's screens that say when they are settled, found again after a change.
        readonly List<ISettledScreen> screens = new List<ISettledScreen>();
        float screensFoundAt = float.NegativeInfinity;
        Canvas canvas;
        RectTransform stage, content;
        Image shade;
        Piece banner, item, from, prompt;
        readonly List<OurTaiko.Chara.OtanimView> rewardArts = new List<OurTaiko.Chara.OtanimView>();
        Func<bool> isUp;

        double Elapsed => Time.realtimeSinceStartupAsDouble - shownAt;

        // What a piece of the drawing is placed and faded by.
        sealed class Piece
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Home;
        }

        void Awake()
        {
            Instance = this;
            inbox = RewardInbox.Shared;
            isUp = () => IsShowing;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over every scene, under the settings (32000) and SceneSwitcher's cover (32767).
            canvas.sortingOrder = 31000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            gameObject.layer = LayerMask.NameToLayer("UI");

            var root = (RectTransform)transform;
            shade = new GameObject("Shade", typeof(RectTransform)).AddComponent<Image>();
            shade.rectTransform.SetParent(root, false);
            shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.sizeDelta = Vector2.zero;
            shade.color = new Color(0, 0, 0, 0);
            shade.raycastTarget = true;
            shade.gameObject.AddComponent<PointerRelay>().Clicked = Don;

            // The 1920x1080 stage, whole on any screen shape (Expand), centred.
            stage = SkinUi.Rect("Stage", root, 1920, 1080);
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            canvas.enabled = false;

            ModalLayers.Register(isUp);
            SceneManager.activeSceneChanged += SceneChanged;
        }

        void OnDestroy()
        {
            ModalLayers.Unregister(isUp);
            SceneManager.activeSceneChanged -= SceneChanged;
            if (Instance == this) Instance = null;
        }

        void SceneChanged(Scene from, Scene to)
        {
            calmSince = float.NaN;
            screens.Clear();
            screensFoundAt = float.NegativeInfinity;
        }

        // Whether the screen is calm now: one of Shows, its own show over, nothing else over it or coming.
        bool IsCalm()
        {
            var scene = SceneManager.GetActiveScene();
            if (!Shows.Contains(scene.name) || SettingsOverlay.IsOpen) return false;
            var switcher = SceneSwitcher.Instance;
            if (switcher != null && (switcher.IsSwitching || switcher.IsCovered)) return false;
            // A screen's script may come a little after its scene: look again now and then until found.
            screens.RemoveAll(screen => !(screen is MonoBehaviour alive) || alive == null);
            if (screens.Count == 0 && Time.realtimeSinceStartup - screensFoundAt > 0.25f)
            {
                screensFoundAt = Time.realtimeSinceStartup;
                screens.AddRange(FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                    .Where(b => b.gameObject.scene == scene).OfType<ISettledScreen>());
            }
            return screens.All(screen => screen.IsSettled);
        }

        // Whether a reward may come up now: the screen has been calm for QuietSeconds.
        bool MayShow()
        {
            if (!IsCalm()) { calmSince = float.NaN; return false; }
            if (float.IsNaN(calmSince)) calmSince = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - calmSince >= QuietSeconds;
        }

        void Update()
        {
            inbox.Pump();
            if (!IsShowing)
            {
                if (inbox.Queue.Count > 0 && MayShow()) Begin(inbox.Queue.ToList());
                return;
            }
            // A screen it does not show on came (a timer elsewhere): it goes until one that allows it.
            if (!Shows.Contains(SceneManager.GetActiveScene().name)) { Hide(); return; }
            if (!SettingsOverlay.IsOpen && DonDown()) Don();
            Animate();
        }

        static bool DonDown() => InputManager.P1.DonDown || InputManager.P2.DonDown;

        void Begin(List<Reward> rewards)
        {
            batch.Clear();
            batch.AddRange(rewards);
            // Kinds nobody here can draw are delivered all the same.
            var unknown = batch.Where(r => !CanDraw(r)).ToList();
            if (unknown.Count > 0) inbox.Shown(unknown);
            batch.RemoveAll(unknown.Contains);
            if (batch.Count == 0) return;
            index = 0;
            canvas.enabled = true;
            Show();
        }

        // The ones still to come stay queued; it comes back on the next screen that allows it.
        void Hide()
        {
            var shown = batch.Take(index).ToList();
            if (shown.Count > 0) inbox.Shown(shown);
            batch.Clear();
            Clear();
            canvas.enabled = false;
        }

        // Puts it away without marking anything (a test's end).
        public void Reset()
        {
            batch.Clear();
            Clear();
            canvas.enabled = false;
        }

        // The canvas it draws on, for captures.
        public Canvas Canvas => canvas;
        public RectTransform Stage => stage;

        // Don or a tap: the reward on screen at once, or the next, or close.
        public void Don()
        {
            if (!IsShowing) return;
            if (!IsReady) { shownAt = Time.realtimeSinceStartupAsDouble - ReadyAt; Animate(); return; }
            index++;
            if (index < batch.Count) { Show(); return; }
            inbox.Shown(batch.ToList());
            batch.Clear();
            Clear();
            canvas.enabled = false;
            // This press closed it; the scene must not take it too.
            ModalLayers.Released();
        }

        void Clear()
        {
            if (content != null) Destroy(content.gameObject);
            content = null; banner = item = from = prompt = null; rewardArts.Clear();
            shade.color = new Color(0, 0, 0, 0);
        }

        // ---------------------------------------------------------------- drawing

        static bool CanDraw(Reward reward) => reward.Kind == Reward.DonItemKind ? reward.DonPart.Length > 0 && reward.DonItemId > 0
            : reward.Kind == Reward.TitleKind ? reward.TitleName("en").Length > 0
            : reward.Kind == Reward.DanKind && reward.DanIndex >= 0 && reward.DanIndex < PlayerInfo.DanCount;

        void Show()
        {
            Clear();
            content = SkinUi.Rect("Content", stage, 1920, 1080);
            content.TopLeft(0, 0);
            shownAt = Time.realtimeSinceStartupAsDouble;
            var reward = batch[index];
            var art = MultiArt.Load();
            string language = SettingManager.EnsureInstance().Settings.general.Language;

            // The banner: the ribbon with what kind of reward it is.
            banner = Place(SkinUi.Image("Banner", content, art.ribbon, 760, 150), 960, 300);
            banner.Rect.GetComponent<Image>().color = new Color(0.86f, 0.2f, 0.16f);
            Text(banner.Rect, L10n.Get("Online", reward.Kind == Reward.DanKind ? "rewards.dan"
                : reward.Kind == Reward.DonItemKind ? "rewards.donItem" : "rewards.title"), 64, 380, 72);

            if (reward.Kind == Reward.TitleKind) DrawTitle(reward, language);
            else if (reward.Kind == Reward.DonItemKind) DrawDonItem(reward, language);
            else DrawDan(reward);

            int verdict = reward.Data["verdict"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (int)reward.Data["verdict"] : 0;
            string source = reward.Kind == Reward.DanKind ? DanText.RankName(reward.DanIndex) + "　" + DanResultFlow.VerdictName(verdict)
                : reward.CourseTitle.Length > 0 ? L10n.Format("Online", "rewards.fromCourse", reward.CourseTitle)
                : reward.Service == "rule" ? L10n.Get("Online", "rewards.fromRule")
                : reward.Service == "admin" ? L10n.Get("Online", "rewards.fromAdmin") : "";
            string description = reward.Kind == Reward.TitleKind ? reward.TitleDescription : "";
            var lines = SkinUi.Rect("From", content, 1400, 140);
            from = Place(lines, 960, 790);
            if (source.Length > 0) Text(lines, source, 40, 700, 40, PlateUi.Gold);
            if (description.Length > 0) Text(lines, description, 34, 700, 96);

            // Whose it is, when both seats may have some, and how many there are.
            if (reward.Seat == 1 || batch.Any(r => r.Seat != reward.Seat)) Corner((reward.Seat + 1) + "P", 120);
            if (batch.Count > 1) Corner((index + 1) + " / " + batch.Count, 1800);

            var next = SkinUi.Rect("Next", content, 600, 80);
            prompt = Place(next, 1560, 1000);
            Text(next, L10n.Get("Online", index < batch.Count - 1 ? "rewards.next" : "rewards.close"), 40, 300, 40);
            Animate();
        }

        // The title on the player's own nameplate: the game's Nameplate prefab with their name and
        // 段位, wearing the title with its layers (or its style's, given before titles had them).
        void DrawTitle(Reward reward, string language)
        {
            const float width = NameplateLayout.Width * Scale, height = NameplateLayout.Height * Scale;
            var holder = SkinUi.Rect("Title", content, width, height);
            item = Place(holder, 960, 560);
            var plate = Instantiate(Resources.Load<NameplateView>(NameplateView.Resource), holder, false);
            plate.Place(0, 0);
            plate.RectTransform.localScale = new Vector3(Scale, Scale, 1);
            var players = PlayerInfoController.EnsureInstance();
            var info = (reward.Seat == 1 ? players.SecondInfo : players.Info).Clone();
            info.title = reward.TitleName(language);
            info.plateState = info.plateTitle = "auto";
            info.profileBand = reward.TitleStyle;
            info.SetProfileLayers(reward.TitleLayers);
            plate.Pin(info, reward.Seat);
        }

        // A part of the 3D Don: its picture on a plate (the path the site described it with),
        // and below it the part and its name.
        void DrawDonItem(Reward reward, string language)
        {
            const float side = 300;
            var holder = SkinUi.Rect("DonItem", content, side, side + 90);
            item = Place(holder, 960, 560);
            var plate = SkinUi.Image("Plate", holder, MultiArt.Load().plate, side, side);
            plate.rectTransform.Center(side / 2, side / 2);
            plate.color = PlateUi.Paper;
            var picture = SkinUi.Image("Picture", holder, null, side - 40, side - 40);
            picture.rectTransform.Center(side / 2, side / 2);
            picture.preserveAspect = true;
            picture.enabled = false;
            if (reward.DonPicture.Length > 0) _ = ShowPictureAsync(reward, reward.DonPicture, picture);
            string part = L10n.Get("Online", "rewards.donPart." + reward.DonPart);
            Text(holder, part + "・" + reward.DonName(language), 48, side / 2, side + 50);
        }

        const int PictureLimit = 2 * 1024 * 1024;

        // The picture once it has come (from the player's root, else the canonical one), if this
        // reward is still on screen; nothing when it cannot.
        async System.Threading.Tasks.Task ShowPictureAsync(Reward reward, string path, Image target)
        {
            try
            {
                var address = await Distribution.LocateAsync(path, reward.Site.Length > 0 ? reward.Site : ServerConfig.Site);
                var bytes = await Distribution.FetchAsync(address, async url =>
                {
                    using var response = await OurTaikoHttpClient.GetAsync(url, TimeSpan.FromSeconds(15));
                    if (!response.IsSuccessStatusCode) throw new System.Net.Http.HttpRequestException("HTTP " + (int)response.StatusCode);
                    var body = await response.Content.ReadAsByteArrayAsync();
                    return body.Length <= PictureLimit ? body : throw new System.IO.InvalidDataException("too large");
                });
                if (target == null || Current != reward) return;
                var texture = Pictures.Decode(bytes, PictureLimit, 512);
                if (texture == null) return;
                texture.filterMode = FilterMode.Point;
                target.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                target.enabled = true;
            }
            catch (Exception error) { Log.Info("Rewards", "The picture of a Don part did not come: " + error.Message); }
        }

        void DrawDan(Reward reward)
        {
            const float width = 72 * 4, height = 40 * 4;
            var holder = SkinUi.Rect("Dan", content, width, height);
            item = Place(holder, 960, 560);
            int dan = reward.DanIndex;
            var info = new PlayerInfo { dan = dan, danCombo = reward.DanCombo, gold = reward.DanGold };
            if (info.HasDan)
            {
                var asset = PlateComposition.RankAsset(info, out string clip);
                ShowRewardArt(holder, asset, clip, 4, new Vector2(-NameplateLayout.DanX * 4, NameplateLayout.DanY * 4));
            }
            else Text(holder, DanText.RankName(dan), 96, width / 2, height / 2, reward.DanGold ? PlateUi.Gold : Color.white);
        }

        void ShowRewardArt(RectTransform holder, OurTaiko.Chara.OtanimAsset asset, string clip, float scale, Vector2 offset)
        {
            var art = PlateComposition.CreatePicture("Reward art", holder);
            rewardArts.Add(art);
            var rect = (RectTransform)art.transform;
            rect.anchoredPosition = offset;
            rect.localScale = new Vector3(scale, scale, 1);
            art.Use(asset, clip);
            art.Show(0);
        }

        TextMeshProUGUI Text(Transform parent, string text, float size, float x, float y, Color? color = null, bool dark = false)
        {
            var label = SkinUi.Text("Text", parent, size);
            label.color = color ?? Color.white;
            if (dark) label.UseUiFont();
            label.rectTransform.Center(x, y);
            label.text = text ?? "";
            label.Squeeze(1300);
            return label;
        }

        void Corner(string text, float x)
        {
            var label = Text(content, text, 40, x, 80);
            label.alignment = TextAlignmentOptions.Center;
        }

        Piece Place(Graphic graphic, float x, float y) => Place(graphic.rectTransform, x, y);

        static Piece Place(RectTransform rect, float x, float y)
        {
            rect.Center(x, y);
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = false;
            return new Piece { Rect = rect, Group = group, Home = rect.anchoredPosition };
        }

        // ---------------------------------------------------------------- timing

        // Comes past its place by `over` of the way, then settles (an ease-out with overshoot).
        public static double BackOut(double t, double over = 1.7)
        {
            t = Math.Min(1, Math.Max(0, t)) - 1;
            return 1 + (over + 1) * t * t * t + over * t * t;
        }

        static double Phase(double elapsed, double at, double seconds) => Math.Min(1, Math.Max(0, (elapsed - at) / seconds));

        void Animate()
        {
            if (content == null) return;
            double t = Elapsed;
            // Blended in linear light: 0.82 looks about as dark as the settings' 0.62 shade over its panel.
            shade.color = new Color(0, 0, 0, ShadeAlpha * (float)Phase(t, 0, DimSeconds));

            double b = Phase(t, BannerAt, BannerSeconds);
            banner.Group.alpha = b > 0 ? 1 : 0;
            banner.Rect.anchoredPosition = banner.Home + new Vector2(0, (float)((1 - BackOut(b)) * 600));

            double i = Phase(t, ItemAt, ItemSeconds);
            item.Group.alpha = i > 0 ? 1 : 0;
            // A badge or a part spins up from small; a title's band slides in.
            if (Current.Kind != Reward.TitleKind)
            {
                float s = (float)(0.3 + 0.7 * BackOut(i, 2.2));
                item.Rect.localScale = new Vector3(s, s, 1);
                item.Rect.localRotation = Quaternion.Euler(0, 0, (float)((1 - BackOut(i, 2.2)) * 25));
            }
            else item.Rect.anchoredPosition = item.Home + new Vector2((float)((1 - BackOut(i, 1.2)) * 1600), 0);

            foreach (var art in rewardArts)
                if (art.Clip?.Frames.Length > 1) art.Show(t);

            from.Group.alpha = (float)Phase(t, FromAt, FromSeconds);
            // "don: next" blinks once a second after it shows.
            prompt.Group.alpha = t < ReadyAt ? 0 : 0.65f + 0.35f * Mathf.Cos((float)(t - ReadyAt) * Mathf.PI * 2);
        }
    }
}
