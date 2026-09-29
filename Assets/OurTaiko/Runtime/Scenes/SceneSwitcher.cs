using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OurTaiko
{
    public sealed class SceneSwitcher : MonoBehaviour
    {
        public const string MenuScene = "SceneSwitcher", GameScene = "PlayScene";
        public static SceneSwitcher Instance { get; private set; }
        public SongDefinition SelectedSong { get; private set; }
        public bool AutoPlay { get; private set; }
        public bool IsSwitching { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public static SceneSwitcher EnsureInstance()
        {
            if (Instance == null) new GameObject(nameof(SceneSwitcher)).AddComponent<SceneSwitcher>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            // VSync takes precedence over targetFrameRate on desktop platforms.
            QualitySettings.vSyncCount = 0;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            Application.targetFrameRate = 120;
        }

        public void Play(SongDefinition song, bool autoPlay = false)
        {
            if (IsSwitching || song == null) return;
            SelectedSong = song; AutoPlay = autoPlay;
            StartCoroutine(Load(GameScene));
        }
        public void Restart() { if (!IsSwitching) StartCoroutine(Load(GameScene)); }
        public void ReturnToMenu() { if (!IsSwitching) StartCoroutine(Load(MenuScene)); }

        IEnumerator Load(string scene)
        {
            IsSwitching = true;
            var operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (operation != null) yield return operation;
            IsSwitching = false;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
