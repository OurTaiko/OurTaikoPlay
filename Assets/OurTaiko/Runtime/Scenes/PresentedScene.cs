using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The first scene: a "Presented by OurTaiko" title card on black, in the manner of a publisher
    // card. Meanwhile OnlineManager signs in to the built-in servers, loads their song lists and
    // downloads each signed-in player's 3D Don (with a bar under the line while files come in), and
    // the line at the bottom follows it. The card stays for at least `minimumSeconds` and until
    // that work ends; a click or tap skips it, interrupting the work where it is, and what is
    // missing then can be loaded from Settings > OurTaiko Accounts. Entry follows.
    public sealed class PresentedScene : MonoBehaviour
    {
        public CanvasGroup card;
        public TMP_Text progress, hint;
        public PointerRelay touch;
        [Min(0)] public float fadeSeconds = 1.2f, minimumSeconds = 3.5f;

        public bool HasLeft { get; private set; }
        public bool Skipped { get; private set; }

        SceneSwitcher switcher;
        OnlineManager online;
        CancellationTokenSource cancel;
        Task work;
        double startedAt;
        // The 3D Don's download: a track and its fill under the progress line.
        RectTransform bar, barFill;
        const float BarWidth = 800, BarHeight = 8;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            online = OnlineManager.EnsureInstance();
            card.alpha = 0;
            progress.text = "";
            touch.Clicked = Skip;
            cancel = new CancellationTokenSource();
            MakeBar();
        }

        void MakeBar()
        {
            var parent = progress.rectTransform.parent;
            bar = Box(parent, "DonDownload", new Color(1, 1, 1, 0.2f));
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0, 1);
            bar.anchoredPosition = new Vector2((1920 - BarWidth) / 2, -984);
            bar.sizeDelta = new Vector2(BarWidth, BarHeight);
            barFill = Box(bar, "Fill", Color.white);
            barFill.anchorMin = barFill.anchorMax = barFill.pivot = new Vector2(0, 0.5f);
            barFill.sizeDelta = new Vector2(0, BarHeight);
            bar.gameObject.SetActive(false);
        }

        static RectTransform Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        // The line and the bar while the Don's files download; false otherwise.
        bool ShowDonDownload()
        {
            bool downloading = Chara.DonDownloads.Downloading && online.DonActivitySeat.Length > 0;
            bar.gameObject.SetActive(downloading && !Skipped);
            if (!downloading || Skipped) return false;
            long got = Chara.DonDownloads.Received, all = Chara.DonDownloads.Expected;
            barFill.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(all > 0 ? (float)got / all : 0), BarHeight);
            progress.text = L10n.Format("Online", "activity.donDownload", online.DonActivitySeat,
                (got / 1048576.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                (all / 1048576.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        void Start()
        {
            startedAt = GameTimeline.FrameTime;
            work = online.StartUpAsync(cancel.Token);
        }

        void OnDestroy()
        {
            cancel?.Cancel();
            cancel?.Dispose();
        }

        void Update()
        {
            if (HasLeft || work == null) return;
            double elapsed = GameTimeline.FrameTime - startedAt;
            card.alpha = fadeSeconds <= 0 ? 1 : Mathf.Clamp01((float)(elapsed / fadeSeconds));
            if (!work.IsCompleted)
            {
                if (!ShowDonDownload()) progress.text = Skipped ? L10n.Get("Menu", "presented.cancelling") : online.Activity;
                return;
            }
            bar.gameObject.SetActive(false);
            progress.text = "";
            if (!Skipped && elapsed < minimumSeconds) return;
            HasLeft = true;
            switcher.SwitchScene(SceneSwitcher.EntryScene);
        }

        // Stops the sign-in or the song list request that is running and moves on.
        public void Skip()
        {
            if (HasLeft || Skipped) return;
            Skipped = true;
            cancel.Cancel();
        }
    }
}
