using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

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
        public AudioSource Sfx { get; private set; }
        Coroutine opening;

        void Awake()
        {
            PrepareAudio();
            // Keyboard/drum confirmation invokes onClick too, so every way of choosing an action sounds once.
            foreach (var button in buttons) button.onClick.AddListener(() => Sfx.PlayAudioOneShot(don));
        }

        // Practice uses menu sounds before this inactive panel receives Awake.
        public void PrepareAudio()
        {
            if (Sfx != null) return;
            var host = new GameObject("PauseMenuAudio");
            if (gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(host, gameObject.scene);
            Sfx = host.AddComponent<AudioSource>();
            Sfx.PrepareAudioEffects(don, ka);
        }

        void OnDestroy()
        {
            if (Sfx != null) Destroy(Sfx.gameObject);
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
                        buttons[SelectedIndex].onClick.Invoke();
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
            if (playSound && moved) Sfx.PlayAudioOneShot(ka);
        }
    }
}
