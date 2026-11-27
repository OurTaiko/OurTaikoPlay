using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko
{
    // Keeps every UI text in the face of its own script (UiFontSet.Pick): Japanese in the Japanese
    // face, Simplified and Traditional Chinese in theirs, and text of no particular script in the
    // interface language's (the Latin face in English). A text whose font is one of the set's gets a FaceSelector as its TMP text
    // preprocessor, which TMP runs whenever it parses the text, before it measures or draws it, so the
    // face changes with the text and layout always measures the face that is drawn.
    //
    // Texts get the selector from SkinUi.UseUiFont, from a scan of every loaded scene, and otherwise
    // the first time TMP draws them (prefabs instantiated at run time), which then redraws them in the
    // right face before they are shown.
    public static class UiFonts
    {
        static UiFontSet set;
        static TextScript? languageScript;
        static readonly List<FaceSelector> selectors = new List<FaceSelector>();

        public static UiFontSet Set => set != null ? set : set = Resources.Load<UiFontSet>(UiFontSet.ResourcePath)
            ?? throw new System.InvalidOperationException("Missing " + UiFontSet.ResourcePath + " font set.");

        // The script that text of no particular script is drawn in: the interface language's. Read from
        // the settings rather than L10n, so choosing a face never forces Localization to initialize;
        // before the settings load it is the system's, and not kept.
        public static TextScript LanguageScript
        {
            get
            {
                if (languageScript.HasValue) return languageScript.Value;
                var settings = SettingManager.Instance;
                if (settings == null) return ScriptOf(GeneralSettings.SystemLanguage());
                languageScript = ScriptOf(settings.Settings.general.Language);
                return languageScript.Value;
            }
        }

        // English text is drawn in the Latin face; Japanese and any other language in the Japanese face,
        // which the skin was drawn with.
        public static TextScript ScriptOf(string language) => language switch
        {
            "zh" => TextScript.SimplifiedChinese,
            "en" => TextScript.Latin,
            _ => TextScript.Japanese,
        };

        // The face for new text in the interface language.
        public static TMP_FontAsset Default => Set.FaceFor(LanguageScript).font;

        public static bool IsUiFont(TMP_FontAsset font) => Set.FaceOf(font) != null;

        // Whether text has its face's black outline (light text) rather than none.
        public static bool IsOutlined(TMP_Text text)
        {
            var face = Set.FaceOf(text.font);
            return face != null && text.fontSharedMaterial == face.outline;
        }

        // Draws text in the face of its current content, outlined or plain, and keeps it in the right
        // face as the text changes.
        public static void Use(TMP_Text text, bool outlined)
        {
            var face = Set.Pick(text.text, LanguageScript);
            text.font = face.font;
            text.fontSharedMaterial = outlined ? face.outline : face.font.material;
            Attach(text);
        }

        // Lets a text that already uses one of the faces follow its content from now on.
        public static void Attach(TMP_Text text)
        {
            if (text == null || text.textPreprocessor is FaceSelector || !IsUiFont(text.font)) return;
            var selector = new FaceSelector(text);
            text.textPreprocessor = selector;
            selectors.Add(selector);
            selector.Select(text.text);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            set = null;
            languageScript = null;
            selectors.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            L10n.LanguageChanged -= OnLanguageChanged;
            L10n.LanguageChanged += OnLanguageChanged;
        }

        // The hooks stay subscribed after the Editor leaves Play mode; they must not touch scenes being edited.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying) return;
            selectors.RemoveAll(selector => selector.Text == null);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    Attach(text);
        }

        // A text TMP drew before it had a selector: select now and, if that changed the face, draw it
        // again at once so the wrong face never reaches the screen.
        static void OnTextChanged(Object changed)
        {
            if (!Application.isPlaying || !(changed is TMP_Text text) || text.textPreprocessor != null || !IsUiFont(text.font)) return;
            var font = text.font;
            Attach(text);
            if (text.font != font) text.ForceMeshUpdate();
        }

        static void OnLanguageChanged()
        {
            languageScript = null;
            if (!Application.isPlaying) return;
            selectors.RemoveAll(selector => selector.Text == null);
            foreach (var selector in selectors) selector.Select(selector.Text.text);
        }

        sealed class FaceSelector : ITextPreprocessor
        {
            public readonly TMP_Text Text;

            public FaceSelector(TMP_Text text) => Text = text;

            public string PreprocessText(string text)
            {
                Select(text);
                return text;
            }

            // Moves the text to the face for content, keeping its outlined or plain material. TMP parses
            // inside the canvas's graphic rebuild, where it ignores new dirty flags: the face then takes
            // effect in the parse under way, and the material goes to the renderer here.
            public void Select(string content)
            {
                var current = Set.FaceOf(Text.font);
                if (current == null) return;
                var face = Set.Pick(content, LanguageScript);
                if (face == current) return;
                bool outlined = Text.fontSharedMaterial == current.outline;
                Text.font = face.font;
                Text.fontSharedMaterial = outlined ? face.outline : face.font.material;
                if (Text is TextMeshProUGUI ui && CanvasUpdateRegistry.IsRebuildingGraphics())
                {
                    ui.canvasRenderer.materialCount = 1;
                    ui.canvasRenderer.SetMaterial(ui.materialForRendering, 0);
                }
            }
        }
    }
}
