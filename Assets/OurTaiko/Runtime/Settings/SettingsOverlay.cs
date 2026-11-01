using System;
using System.Linq;
using System.Threading;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OurTaiko
{
    // The settings overlay, opened over the current scene (Entry's ゲーム設定 board) and kept across
    // loads like SceneSwitcher. While it is open SceneSwitcher.IsInputBlocked is true, so the scene
    // underneath ignores its keys. Drum keys and the menu keys drive SettingsNavigator; held keys
    // repeat, faster the longer they are held. Each change is saved at once through SettingManager.
    // Closing applies pending audio device changes by reloading the scene behind the fade, like the
    // old settings scene did on its way back to Entry.
    public sealed class SettingsOverlay : MonoBehaviour
    {
        public const string ResourceName = "SettingsOverlay";
        public const float RepeatDelay = 0.35f;
        // Seconds between repeats after a key or stepper has been held this long.
        public static float RepeatInterval(float heldFor) => heldFor > 1.5f ? 0.025f : heldFor > 0.8f ? 0.05f : 0.09f;

        public static SettingsOverlay Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.Navigator != null;
        // Also the frame it closed on, so the key that closed it does not act in the scene below.
        public static bool BlocksInput => IsOpen || Time.frameCount <= closedFrame;
        static int closedFrame = -1;
        public static GameSettings CurrentSettings => Instance?.Navigator?.Settings ?? SettingManager.EnsureInstance().Settings;
        public static SettingRow CurrentRow(int index)
        {
            var page = Instance?.Navigator?.Page;
            return page != null && index >= 0 && index < page.Rows.Count ? page.Rows[index] : null;
        }

        public AudioClip don, ka, previewVoice;

        public SettingsNavigator Navigator { get; private set; }
        public SettingsOverlayView View { get; private set; }
        // The footer's message: how an action went, or why the audio device could not change.
        public string Status => status;
        public event Action Closed;

        AudioSource sfx;
        string status = "";
        int ignoreFrame = -1, openFrame = -1;
        InputKey? held;
        float heldAt, nextRepeat;
        // The row of the running settings action (by id: rows are rebuilt on every change); closing cancels it.
        string runningId;
        CancellationTokenSource actionCancel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; closedFrame = -1; }

        public static SettingsOverlay EnsureInstance()
        {
            if (Instance != null) return Instance;
            var prefab = Resources.Load<SettingsOverlay>(ResourceName);
            var overlay = prefab != null ? Instantiate(prefab) : new GameObject().AddComponent<SettingsOverlay>();
            overlay.name = nameof(SettingsOverlay);
            return overlay;
        }

        // Opens over the current scene; does nothing while already open.
        public static SettingsOverlay Open()
        {
            var overlay = EnsureInstance();
            if (overlay.Navigator == null) overlay.Show();
            return overlay;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.activeSceneChanged += OnSceneChanged;
            sfx = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            var canvas = new GameObject("Canvas", typeof(RectTransform));
            canvas.transform.SetParent(transform, false);
            View = canvas.AddComponent<SettingsOverlayView>();
            View.Build();
            View.TapPage = i => Run(() => Navigator.TapPage(i));
            View.TapRow = i => Run(() => Navigator.TapRow(i));
            // A row's own button: focus it and confirm it in one tap.
            View.OpenRow = i => Run(() => Navigator.TapRow(i) is var r && r == SettingsNavigator.Result.Moved ? Navigator.Confirm() : r);
            View.SetRow = (i, value) => Run(() => Navigator.SetRow(i, value));
            View.TapSheet = i => Run(() => Navigator.TapSheet(i));
            View.TapShade = () => { if (Navigator != null && Navigator.Zone is SettingsZone.Sheet or SettingsZone.Capture or SettingsZone.Typing) Run(() => Navigator.Back()); };
            View.SubmitText = text => { ignoreFrame = Time.frameCount; Run(() => Navigator.SubmitText(text)); };
            View.CancelText = () => { ignoreFrame = Time.frameCount; if (Navigator?.Zone == SettingsZone.Typing) Run(() => Navigator.Back()); };
        }

        // A scene loaded underneath (not through the overlay) takes the overlay with the old scene,
        // closing it like Close() does; pending device changes apply once that switch is over.
        void OnSceneChanged(Scene from, Scene to)
        {
            if (Navigator != null) Dismiss();
        }

        void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (Instance == this) Instance = null;
            actionCancel?.Cancel();
        }

        void Show()
        {
            Navigator = new SettingsNavigator(SettingsCatalog.Pages(SoundSettings.Platform), SettingManager.EnsureInstance().Settings);
            status = "";
            openFrame = Time.frameCount;
            held = null;
            View.Open();
            if (don != null && ka != null) sfx.PrepareAudioEffects(don, ka);
            ShowAccounts();
            Refresh();
        }

        void Refresh()
        {
            if (Navigator == null) return;
            var engine = AudioEngine.Instance;
            string sound = engine == null ? null
                : !engine.Available ? "No audio output: " + engine.Failure
                : engine.Silent ? "No output device (silent): " + engine.Failure
                : "Current output: " + engine.Backend;
            bool pending = engine != null && engine.HasPendingDeviceChanges;
            if (pending) sound += "   ·   Output changes apply when you close Settings";
            View.Show(Navigator, status, sound, pending);
        }

        // The sign-out rows show who is signed in; the header shows both seats.
        void ShowAccounts()
        {
            var accounts = OnlineManager.EnsureInstance().Accounts;
            string Name(int seat) => accounts.Seats[seat].IsSignedIn
                ? (accounts.Seats[seat].Nickname.Length > 0 ? accounts.Seats[seat].Nickname : accounts.Seats[seat].Username) : null;
            View.ShowSeats(Name(0), Name(1));
            foreach (var row in Navigator.Pages.SelectMany(p => p.Rows))
                if (row.ActionId != null && row.ActionId.StartsWith(SettingsCatalog.SignOut) && row.Id != runningId)
                {
                    string name = Name(row.ActionId[SettingsCatalog.SignOut.Length] - '0');
                    row.Status = name != null ? "Signed in: " + name : "Not signed in";
                }
        }

        void Update()
        {
            if (Navigator == null) return;
            if (runningId != null)
            {
                string activity = OnlineManager.EnsureInstance().Activity;
                if (activity.Length > 0 && status != activity) { status = activity; Refresh(); }
            }
            var switcher = SceneSwitcher.Instance;
            if (switcher != null && (switcher.IsSwitching || switcher.IsCovered)) return;
            // The key that opened the overlay must not also act in it.
            if (Time.frameCount <= openFrame || Time.frameCount == ignoreFrame) return;
            if (Navigator.Zone == SettingsZone.Capture)
            {
                if (Keyboard.current == null) return;
                foreach (var key in Keyboard.current.allKeys)
                    if (key.wasPressedThisFrame) { Handle(Navigator.CaptureKey(key.keyCode)); break; }
                return;
            }
            // The text field takes every key, the drum letters included.
            if (Navigator.Zone == SettingsZone.Typing) return;

            if (Down(InputKey.LeftDon) || Down(InputKey.RightDon) || Down(InputKey.Confirm)) Confirm();
            else if (Down(InputKey.Back)) Back();
            else if (Press(InputKey.LeftKa) || Press(InputKey.MenuUp)) Move(-1, repeating);
            else if (Press(InputKey.RightKa) || Press(InputKey.MenuDown)) Move(1, repeating);
            else if (Press(InputKey.MenuLeft)) Side(-1);
            else if (Press(InputKey.MenuRight)) Side(1);
            if (held is InputKey key0 && !InputManager.GetKey(key0)) held = null;
        }

        // The keys' actions, also for tests: don, ka (left -1, right +1), left/right arrows and Back.
        public void Confirm() => Run(() => Navigator.Confirm());
        public void Move(int delta, bool repeat = false) => Run(() => Navigator.Move(delta, repeat));
        public void Side(int delta) => Run(() => Navigator.Side(delta));
        public void Back() => Run(() => Navigator.Back());

        static bool Down(InputKey key) => InputManager.GetKeyDown(key);

        // A first press, or a repeat while the same key is held (then repeating is set).
        bool repeating;
        bool Press(InputKey key)
        {
            float now = Time.unscaledTime;
            repeating = false;
            if (InputManager.GetKeyDown(key)) { held = key; heldAt = now; nextRepeat = now + RepeatDelay; return true; }
            if (held != key || !InputManager.GetKey(key) || now < nextRepeat) return false;
            nextRepeat = now + RepeatInterval(now - heldAt);
            repeating = true;
            return true;
        }

        void Run(Func<SettingsNavigator.Result> action)
        {
            if (Navigator == null) return;
            var zone = Navigator.Zone;
            var result = action();
            if (result != SettingsNavigator.Result.None && result != SettingsNavigator.Result.Action) status = "";
            Handle(result, zone);
        }

        void Handle(SettingsNavigator.Result result, SettingsZone? from = null)
        {
            if (result == SettingsNavigator.Result.None || Navigator == null) return;
            switch (result)
            {
                case SettingsNavigator.Result.Bumped:
                    // Toward the end the list stopped at: down for the last entry, up for the first.
                    View.Bump(Navigator.Zone == SettingsZone.Rail ? (Navigator.PageIndex == 0 ? -1 : 1)
                        : Navigator.Zone == SettingsZone.Sheet ? (Navigator.SheetIndex == 0 ? -1 : 1)
                        : Navigator.Zone == SettingsZone.Rows && Navigator.Row != null && !Navigator.Row.IsAdjustable ? (Navigator.RowIndex == 0 ? -1 : 1) : 0);
                    break;
                case SettingsNavigator.Result.Moved:
                    Play(ka);
                    break;
                case SettingsNavigator.Result.Changed:
                    SettingManager.EnsureInstance().Set(Navigator.Settings);
                    var row = Navigator.Row;
                    if (row?.Id == "drum-volume") sfx.PlayAudioOneShot(don, AudioGroup.Drum);
                    else if (row?.Id == "voice-volume" && previewVoice != null) sfx.PlayAudioOneShot(previewVoice, AudioGroup.Voice);
                    else Play(row?.Kind == SettingKind.Slider || from == SettingsZone.Rows && row?.IsAdjustable == true ? ka : don);
                    break;
                case SettingsNavigator.Result.Action:
                    Play(don);
                    RunAction(Navigator.Row, Navigator.PendingAction, Navigator.PendingText);
                    break;
                case SettingsNavigator.Result.Close:
                    Play(don);
                    Close();
                    return;
                default:
                    Play(don);
                    break;
            }
            Refresh();
        }

        // Leaves the scene underneath as it was, unless the audio device changed: then the scene
        // reloads behind the fade while the new output starts.
        public void Close()
        {
            if (Navigator != null) Dismiss();
        }

        void Dismiss()
        {
            SettingManager.EnsureInstance().Set(Navigator.Settings);
            // The cancelled action is over as far as the overlay is concerned: a reopened overlay may
            // run the next one at once, and the old task leaves everything alone when it returns.
            actionCancel?.Cancel();
            actionCancel = null;
            runningId = null;
            Navigator = null;
            held = null;
            closedFrame = Time.frameCount;
            View.Close();
            var engine = AudioEngine.Instance;
            if (engine != null && engine.HasPendingDeviceChanges) ApplyAudio(engine);
            Closed?.Invoke();
        }

        async void ApplyAudio(AudioEngine engine)
        {
            var switcher = SceneSwitcher.EnsureInstance();
            try
            {
                // A switch already under way (the one that dismissed the overlay) finishes first;
                // the scene it loaded is then reloaded with the new device.
                while (switcher != null && switcher.IsSwitching)
                {
                    await Awaitable.NextFrameAsync();
                    if (this == null) return;
                }
                await switcher.SwitchSceneAfterFadeAsync(SceneSwitcher.CurrentScene, () => engine.ApplyPendingSettingsAsync());
            }
            catch (Exception error)
            {
                // ApplyPendingSettingsAsync restored the previous device but stopped every sound:
                // reload the scene for its music, then reopen on Sound with the reason.
                Log.Warn("Audio", error.Message);
                try { await switcher.SwitchSceneAsync(SceneSwitcher.CurrentScene); }
                catch (Exception reload) { Log.Warn("Scene", reload.Message); }
                if (this == null || Navigator != null) return;
                Show();
                Navigator.TapPage(Navigator.Pages.ToList().FindIndex(p => p.Id == "sound"));
                Navigator.Confirm();
                status = error.Message;
                Refresh();
            }
        }

        // The username typed above was saved when it was entered, so the manager reads the current
        // one. text is what a password row was given; it is passed on and not kept.
        async void RunAction(SettingRow row, string action, string text)
        {
            var online = OnlineManager.EnsureInstance();
            if (runningId != null || online.IsBusy) { status = "Still busy with the previous request."; Refresh(); return; }
            bool account = action.StartsWith(SettingsCatalog.SignIn) || action.StartsWith(SettingsCatalog.SignOut);
            int seat = account ? action[action.Length - 1] - '0' : 0;
            var settings = Navigator.Settings;
            string username = seat == 0 ? settings.accounts.p1Username : settings.accounts.p2Username;
            if (action.StartsWith(SettingsCatalog.SignIn) && (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(text)))
            {
                status = string.IsNullOrEmpty(username) ? $"Enter {seat + 1}P's username first." : "";
                Refresh();
                return;
            }
            runningId = row.Id;
            row.Status = "Working…";
            Refresh();
            actionCancel = new CancellationTokenSource();
            var cancel = actionCancel;
            try
            {
                if (action == SettingsCatalog.RefreshSongList)
                {
                    await online.LoadSongListAsync(cancel.Token);
                    status = online.SongListSummary;
                }
                else if (action.StartsWith(SettingsCatalog.SignIn))
                {
                    try
                    {
                        await online.SignInAsync(seat, username, text, cancel.Token);
                        status = $"{seat + 1}P signed in as {online.Accounts.Seats[seat].Nickname}.   ·   {online.LoginSummary}";
                    }
                    catch (Exception error) when (!cancel.IsCancellationRequested)
                    {
                        status = $"{seat + 1}P: " + OnlineManager.Describe(error);
                    }
                }
                else if (action.StartsWith(SettingsCatalog.SignOut))
                {
                    bool was = online.Accounts.Seats[seat].IsSignedIn;
                    await online.SignOutAsync(seat, cancel.Token);
                    status = was ? $"{seat + 1}P signed out." : $"{seat + 1}P is not signed in.";
                }
                else
                {
                    await online.LoginAsync(cancel.Token);
                    status = online.LoginSummary;
                }
            }
            catch (Exception error) when (!cancel.IsCancellationRequested) { status = error.Message; Log.Exception("Settings", error); }
            catch (Exception) { }
            bool cancelled = cancel.IsCancellationRequested;
            cancel.Dispose();
            if (actionCancel == cancel) actionCancel = null;
            // Cancelled means the overlay was dismissed meanwhile, which already cleared the action.
            if (this == null || cancelled) return;
            runningId = null;
            (RowById(row.Id) ?? row).Status = account ? "" : "Done";
            if (Navigator == null) return;
            ShowAccounts();
            Refresh();
        }

        SettingRow RowById(string id) => Navigator?.Pages.SelectMany(p => p.Rows).FirstOrDefault(r => r.Id == id);

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }
    }
}
