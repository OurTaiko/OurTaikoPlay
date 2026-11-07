using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko
{
    // A modal menu: only its own buttons and logical keyboard inputs are active while shown.
    public sealed class PauseMenuView : MonoBehaviour
    {
        public const float FadeDuration = 0.5f;
        public CanvasGroup group;
        public UnityEngine.UI.Button[] buttons;
        // The menu sounds every other menu uses: ka when the selection moves, don when an action is taken.
        public AudioClip don, ka;
        public int SelectedIndex { get; private set; }
        public bool IsClosing { get; private set; }
        // The menu's sounds live on their own scene object: Hide deactivates this panel, which would cut a
        // don still ringing (AudioBus.OnDisable stops everything), and the scene's hitAudio stays silent under pause.
        // Made on first use: the practice panel sounds these before this inactive panel receives Awake.
        public AudioSource Sfx => sfx != null ? sfx : sfx = NewSfx();
        AudioSource sfx;
        public Button ModeSwitchButton { get; private set; }
        Coroutine opening;
        bool awoke;

        // The selection moved / an action was chosen; the practice panel shares them.
        public void PlayMove() => Sfx.PlayAudioOneShot(ka);
        public void PlayConfirm() => Sfx.PlayAudioOneShot(don);

        void Awake()
        {
            awoke = true;
            if (sfx == null) sfx = NewSfx();
            // Keyboard/drum confirmation invokes onClick too, so every way of choosing an action sounds once.
            foreach (var button in buttons) button.onClick.AddListener(PlayConfirm);
        }

        AudioSource NewSfx()
        {
            var host = new GameObject("PauseMenuAudio");
            if (gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(host, gameObject.scene);
            var source = host.AddComponent<AudioSource>();
            source.PrepareAudioEffects(don, ka);
            return source;
        }

        void OnDestroy()
        {
            if (sfx != null) Destroy(sfx.gameObject);
        }

        public void ConfigureModeSwitch(bool practice, UnityAction action)
        {
            if (ModeSwitchButton != null) return;
            var back = buttons[2];
            ModeSwitchButton = Instantiate(back, back.transform.parent);
            ModeSwitchButton.name = "ModeSwitchButton";
            ModeSwitchButton.onClick = new Button.ButtonClickedEvent();
            ModeSwitchButton.onClick.AddListener(action);
            // Awake sounds every button in `buttons` once; a button added after it sounds itself.
            if (awoke) ModeSwitchButton.onClick.AddListener(PlayConfirm);
            var label = ModeSwitchButton.GetComponentInChildren<TMP_Text>(true);
            label.text = practice ? "Switch to Normal Play" : "Switch to Practice";
            label.fontSize = 42;
            buttons = new[] { buttons[0], buttons[1], ModeSwitchButton, back };
            for (int i = 0; i < buttons.Length; i++)
            {
                var rect = (RectTransform)buttons[i].transform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, 100);
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -202 - i * 110);
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            IsClosing = false;
            group.alpha = 0;
            group.blocksRaycasts = true;
            group.interactable = true;
            // Navigation and submit are handled once through InputManager; the UI input module
            // remains responsible for mouse/touch clicks, without a second keyboard submit.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Select(0);
            opening = StartCoroutine(Fade(1));
        }

        public IEnumerator Hide()
        {
            if (opening != null) { StopCoroutine(opening); opening = null; }
            IsClosing = true;
            group.interactable = false;
            // Keep the blocker until the last frame of the fade, including transparent areas.
            group.blocksRaycasts = true;
            yield return Fade(0);
            group.blocksRaycasts = false;
            gameObject.SetActive(false);
            IsClosing = false;
        }

        IEnumerator Fade(float target)
        {
            float from = group.alpha;
            double began = GameTimeline.FrameTime;
            while (GameTimeline.FrameTime - began < FadeDuration)
            {
                group.alpha = Mathf.Lerp(from, target,
                    (float)((GameTimeline.FrameTime - began) / FadeDuration));
                yield return null;
            }
            group.alpha = target;
        }

        public void HandleInput()
        {
            if (IsClosing || !group.interactable) return;
            // One menu action per frame, preserving the order captured by InputManager.
            foreach (var press in InputManager.PressesThisFrame)
            {
                switch (press.Key)
                {
                    case InputKey.MenuUp:
                    case InputKey.MenuLeft:
                    case InputKey.LeftKa:
                        Select((SelectedIndex + buttons.Length - 1) % buttons.Length, true);
                        return;
                    case InputKey.MenuDown:
                    case InputKey.MenuRight:
                    case InputKey.RightKa:
                        Select((SelectedIndex + 1) % buttons.Length, true);
                        return;
                    case InputKey.Confirm:
                    case InputKey.LeftDon:
                    case InputKey.RightDon:
                        if (buttons[SelectedIndex].IsInteractable()) buttons[SelectedIndex].onClick.Invoke();
                        return;
                }
            }
        }

        void Select(int index, bool playSound = false)
        {
            bool moved = SelectedIndex != index;
            SelectedIndex = index;
            for (int i = 0; i < buttons.Length; i++)
            {
                var selection = buttons[i].transform.Find("Selection");
                if (selection != null) selection.gameObject.SetActive(i == index);
            }
            if (playSound && moved) PlayMove();
        }
    }
}
