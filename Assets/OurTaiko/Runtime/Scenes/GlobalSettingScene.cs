using System.Linq;
using System.Threading;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // GlobalSettingScene, reached from Entry's ゲーム設定 board. Drum keys drive SettingsMenu: ka
    // (left = up, right = down) moves within the focused list, don confirms; Esc steps back one
    // level. Taps work like SongSelect's boards (tap another row to move to it, tap the focused row
    // to confirm); the choice popup takes a tap on a choice to apply it and a tap outside it to
    // close it, and both lists also take vertical swipes. A text item is typed into the popup's
    // field: Enter applies it, Esc or a tap outside cancels. An action row (refreshing the song
    // list or the login) runs when confirmed and reports on the line above the items. Each
    // applied choice is saved at once through SettingManager; leaving returns to Entry.
    public sealed class GlobalSettingScene : MonoBehaviour
    {
        public GlobalSettingView view;
        public AudioSource bgm, sfx;
        public AudioClip don, ka, previewVoice;

        public SettingsMenu Menu { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;
        int captureFrame;
        // TMP_InputField handles Enter/Esc itself; the frame it does so ignores the same key here.
        int textKeyFrame = -1;
        // The running settings action; leaving the scene cancels it.
        SettingItem running;
        CancellationTokenSource actionCancel;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            var settings = SettingManager.EnsureInstance().Settings;
            Menu = new SettingsMenu(SettingsMenu.Catalog(), settings);
            view.Bind(i => Run(() => Menu.TapType(i)), i => Run(() => Menu.TapItem(i)), i => Run(() => Menu.TapChoice(i)),
                d => Run(() => Menu.SwipeTypes(d)), d => Run(() => Menu.SwipeItems(d)),
                () => { if (Menu.Focus == SettingsFocus.Choice) Run(() => Menu.Back()); },
                d => { if (Menu.Focus == SettingsFocus.Choice) Ka(d); });
            if (view.textField != null)
            {
                view.textField.onSubmit.AddListener(text =>
                {
                    textKeyFrame = Time.frameCount;
                    var item = Menu.CurrentItem;
                    Handle(Menu.SubmitText(text));
                    if (item?.SubmitActionId != null && Menu.CurrentItem == item) RunAction(item, item.SubmitActionId, text);
                });
                // Ending the edit any other way (Esc, a tap elsewhere) leaves the value as it was.
                view.textField.onEndEdit.AddListener(_ => { textKeyFrame = Time.frameCount; if (Menu.EditingText) Handle(Menu.Back()); });
            }
            ShowAccounts();
            view.Show(Menu);
            switcher.SceneChanging += OnSceneChanging;
        }

        // The sign-out rows show who is signed in.
        void ShowAccounts()
        {
            var accounts = OnlineManager.EnsureInstance().Accounts;
            foreach (var item in Menu.Types.SelectMany(t => t.Items))
                if (item.ActionId != null && item.ActionId.StartsWith(SettingsMenu.SignOutAction) && item != running)
                {
                    var account = accounts.Seats[item.ActionId[SettingsMenu.SignOutAction.Length] - '0'];
                    item.Status = account.IsSignedIn ? "Signed in: " + (account.Nickname.Length > 0 ? account.Nickname : account.Username) : "Not signed in";
                }
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            if (bgm == null || bgm.clip == null) return;
            sfx.PrepareAudioEffects(don, ka);
            bgm.loop = true;
            bgm.PlayAudio();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
            actionCancel?.Cancel();
        }

        void OnSceneChanging(string scene)
        {
            if (bgm != null) bgm.StopAudio();
        }

        void Update()
        {
            if (running != null)
            {
                string activity = OnlineManager.EnsureInstance().Activity;
                if (activity.Length > 0 && view.status != activity) { view.status = activity; view.Show(Menu); }
            }
            if (switcher.IsInputBlocked || HasLeft) return;
            if (Menu.CapturingKey)
            {
                if (Time.frameCount <= captureFrame || Keyboard.current == null) return;
                foreach (var key in Keyboard.current.allKeys)
                    if (key.wasPressedThisFrame) { Handle(Menu.CaptureKey(key.keyCode)); break; }
                return;
            }
            // Typed letters also reach the drum keys; the field takes every key while it is open.
            if (Menu.EditingText || Time.frameCount == textKeyFrame) return;
            if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm))
                Don();
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp) || InputManager.GetKeyDown(InputKey.MenuLeft))
                Ka(-1);
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown) || InputManager.GetKeyDown(InputKey.MenuRight))
                Ka(1);
            else if (InputManager.GetKeyDown(InputKey.Back))
                Run(() => Menu.Back());
        }

        public void Don() => Run(() => Menu.Don());
        public void Ka(int delta) => Run(() => Menu.Ka(delta));

        void Run(System.Func<SettingsMenu.Result> action)
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            bool capturing = Menu.CapturingKey, typing = Menu.EditingText;
            Handle(action());
            if (!capturing && Menu.CapturingKey) captureFrame = Time.frameCount;
            if (!typing && Menu.EditingText) view.BeginText(Menu.CurrentItem.GetText(Menu.Settings), Menu.CurrentItem.IsSecret);
        }
        void Handle(SettingsMenu.Result result)
        {
            if (result == SettingsMenu.Result.None || switcher.IsInputBlocked || HasLeft) return;
            // Moves answer with the rim sound, everything that confirms or steps with the face.
            if (result == SettingsMenu.Result.Changed)
            {
                SettingManager.EnsureInstance().Set(Menu.Settings);
                if (Menu.CurrentItem.Label == "Drum Volume") sfx.PlayAudioOneShot(don, AudioGroup.Drum);
                else if (Menu.CurrentItem.Label == "Voice Volume" && previewVoice != null) sfx.PlayAudioOneShot(previewVoice, AudioGroup.Voice);
                else Play(don);
            }
            else Play(result == SettingsMenu.Result.Moved ? ka : don);
            if (result == SettingsMenu.Result.Action) RunAction(Menu.CurrentItem, Menu.CurrentItem.ActionId);
            if (result == SettingsMenu.Result.Exit)
            {
                HasLeft = true;
                SaveAndLeave();
            }
            view.Show(Menu);
        }

        // The username typed above was saved when it was entered, so the manager reads the current
        // one. text is what a password row was given; it is passed on and not kept.
        async void RunAction(SettingItem item, string action, string text = null)
        {
            var online = OnlineManager.EnsureInstance();
            if (running != null || online.IsBusy) { view.status = "Still busy with the previous request."; view.Show(Menu); return; }
            bool account = action.StartsWith(SettingsMenu.SignInAction) || action.StartsWith(SettingsMenu.SignOutAction);
            int seat = account ? action[action.Length - 1] - '0' : 0;
            string username = seat == 0 ? Menu.Settings.accounts.p1Username : Menu.Settings.accounts.p2Username;
            if (action.StartsWith(SettingsMenu.SignInAction) && (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(text)))
            {
                view.status = string.IsNullOrEmpty(username) ? $"Enter {seat + 1}P's username first." : "";
                view.Show(Menu);
                return;
            }
            running = item;
            item.Status = "Working…";
            view.Show(Menu);
            actionCancel = new CancellationTokenSource();
            var cancel = actionCancel;
            try
            {
                if (action == SettingsMenu.RefreshSongListAction)
                {
                    await online.LoadSongListAsync(cancel.Token);
                    view.status = online.SongListSummary;
                }
                else if (action.StartsWith(SettingsMenu.SignInAction))
                {
                    try
                    {
                        await online.SignInAsync(seat, username, text, cancel.Token);
                        view.status = $"{seat + 1}P signed in as {online.Accounts.Seats[seat].Nickname}.   •   {online.LoginSummary}";
                    }
                    catch (System.Exception error) when (!cancel.IsCancellationRequested)
                    {
                        view.status = $"{seat + 1}P: " + OnlineManager.Describe(error);
                    }
                }
                else if (action.StartsWith(SettingsMenu.SignOutAction))
                {
                    bool was = online.Accounts.Seats[seat].IsSignedIn;
                    await online.SignOutAsync(seat, cancel.Token);
                    view.status = was ? $"{seat + 1}P signed out." : $"{seat + 1}P is not signed in.";
                }
                else
                {
                    await online.LoginAsync(cancel.Token);
                    view.status = online.LoginSummary;
                }
            }
            catch (System.Exception error) { view.status = error.Message; Log.Exception("Settings", error); }
            finally { cancel.Dispose(); if (actionCancel == cancel) actionCancel = null; }
            if (this == null) return;
            running = null;
            item.Status = account ? "" : "Done";
            ShowAccounts();
            view.Show(Menu);
        }

        async void SaveAndLeave()
        {
            try
            {
                SettingManager.EnsureInstance().Set(Menu.Settings);
                await switcher.SwitchSceneAfterFadeAsync(SceneSwitcher.EntryScene,
                    () => AudioEngine.EnsureInstance().ApplyPendingSettingsAsync());
            }
            catch (System.Exception error)
            {
                if (this == null) return;
                HasLeft = false;
                Menu.Settings.audio = SettingManager.EnsureInstance().Settings.Clone().audio;
                Menu.RefreshItems();
                view.Show(Menu);
                if (view.outputStatus != null) view.outputStatus.text = error.Message;
                Log.Warn("Audio", error.Message);
                if (bgm != null && bgm.clip != null) bgm.PlayAudio();
            }
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }
    }
}
