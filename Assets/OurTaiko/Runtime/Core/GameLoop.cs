using UnityEngine;

namespace OurTaiko
{
    // Publish the frame's time before collecting input and updating scenes.
    [DefaultExecutionOrder(-32000)]
    sealed class GameLoop : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            InputManager.Initialize();
            GameTimeline.UpdateFrame();
            if (FindAnyObjectByType<GameLoop>() != null) return;
            var root = new GameObject(nameof(GameLoop)) { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(root);
            root.AddComponent<GameLoop>();
        }

        void Update()
        {
            GameTimeline.UpdateFrame();
            InputManager.OnPreUpdate();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        System.Collections.IEnumerator Start()
        {
            var wait = new WaitForSecondsRealtime(1);
            while (true)
            {
                yield return wait;
                int fps = DisplayRefreshRate.CurrentTarget;
                if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;
            }
        }
#endif
    }
}
