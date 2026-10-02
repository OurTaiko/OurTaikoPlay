using UnityEngine;

namespace OurTaiko
{
    // GlobalSettingScene, reached from Entry's ゲーム設定 board. Drum keys drive SettingsMenu: ka
    // (left = up, right = down) moves within the focused list, don confirms; Esc steps back one
    // level. Taps work like SongSelect's boards (tap another row to move to it, tap the focused row
    // to confirm); the choice popup takes a tap on a choice to apply it and a tap outside it to
    // close it, and both lists also take vertical swipes. Each
    // applied choice is saved at once through SettingManager; leaving returns to Entry.
    public sealed class GlobalSettingScene : MonoBehaviour
    {
        public GlobalSettingView view;
        public AudioSource bgm, sfx;
        public AudioClip don, ka;

        public SettingsMenu Menu { get; private set; }
        public bool HasLeft { get; private set; }

        SceneSwitcher switcher;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            Menu = new SettingsMenu(SettingsMenu.Catalog(), SettingManager.EnsureInstance().Settings);
            view.Bind(i => Handle(Menu.TapType(i)), i => Handle(Menu.TapItem(i)), i => Handle(Menu.TapChoice(i)),
                d => Handle(Menu.SwipeTypes(d)), d => Handle(Menu.SwipeItems(d)),
                () => { if (Menu.Focus == SettingsFocus.Choice) Handle(Menu.Back()); });
            view.Show(Menu);
            switcher.SceneChanging += OnSceneChanging;
        }

        void Start()
        {
            if (bgm == null || bgm.clip == null) return;
            bgm.loop = true;
            bgm.Play();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            if (bgm != null) bgm.Stop();
        }

        void Update()
        {
            if (switcher.IsInputBlocked || HasLeft) return;
            if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm))
                Don();
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp) || InputManager.GetKeyDown(InputKey.MenuLeft))
                Ka(-1);
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown) || InputManager.GetKeyDown(InputKey.MenuRight))
                Ka(1);
            else if (InputManager.GetKeyDown(InputKey.Back))
                Handle(Menu.Back());
        }

        public void Don() => Handle(Menu.Don());
        public void Ka(int delta) => Handle(Menu.Ka(delta));

        void Handle(SettingsMenu.Result result)
        {
            if (result == SettingsMenu.Result.None || switcher.IsInputBlocked || HasLeft) return;
            // Moves answer with the rim sound, everything that confirms or steps with the face.
            Play(result == SettingsMenu.Result.Moved ? ka : don);
            if (result == SettingsMenu.Result.Changed) SettingManager.EnsureInstance().Set(Menu.Settings);
            if (result == SettingsMenu.Result.Exit)
            {
                HasLeft = true;
                switcher.SwitchScene(SceneSwitcher.EntryScene);
            }
            view.Show(Menu);
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }
    }
}
