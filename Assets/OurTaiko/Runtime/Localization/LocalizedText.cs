using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OurTaiko
{
    // Shows one string-table entry on the TextMeshPro text beside it and follows language changes. For
    // fixed labels in scenes and prefabs; text that code sets goes through L10n instead. The object
    // keeps its authored text so it reads the same in the Editor.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] string table;
        [SerializeField] string key;

        TMP_Text text;

        public string Table => table;
        public string Key => key;

        public void Set(string tableName, string entryKey)
        {
            table = tableName;
            key = entryKey;
            // In the Editor outside Play mode the object keeps its own text.
            if (Application.isPlaying && isActiveAndEnabled) Apply();
        }

        // Labels that start inactive (boards that slide in later) get no OnEnable until shown, but code
        // may read their text before that: every loaded scene applies its labels, inactive ones included.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var label in FindObjectsByType<LocalizedText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (label.gameObject.scene == scene) label.Apply();
        }

        void OnEnable()
        {
            Apply();
            L10n.LanguageChanged += Apply;
        }

        void OnDisable() => L10n.LanguageChanged -= Apply;

        void Apply()
        {
            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) return;
            if (text == null) text = GetComponent<TMP_Text>();
            text.text = L10n.Get(table, key);
        }
    }
}
