using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // The first scene: a "Presented by OurTaiko" title card on black, in the manner of a publisher
    // card. Meanwhile OnlineManager signs in to the built-in servers and loads their song lists,
    // and the line at the bottom follows it. The card stays for at least `minimumSeconds` and until
    // that work ends; Space (or a tap) skips it, interrupting the work where it is, and what is
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

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            online = OnlineManager.EnsureInstance();
            card.alpha = 0;
            progress.text = "";
            touch.Clicked = Skip;
            cancel = new CancellationTokenSource();
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
                progress.text = Skipped ? "Cancelling…" : online.Activity;
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Skip();
                return;
            }
            progress.text = "";
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Skip();
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
