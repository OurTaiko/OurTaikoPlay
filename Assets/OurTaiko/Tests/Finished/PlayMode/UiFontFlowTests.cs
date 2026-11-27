using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // A UI text that never went through SkinUi (a prefab instantiated at run time) is moved to the face
    // of its content when TMP first draws it, in the same frame (Runtime/Text/UiFonts.cs).
    public sealed class UiFontFlowTests
    {
        [UnityTest]
        public IEnumerator ATextFirstDrawnInTheWrongFaceIsRedrawnInItsOwnBeforeItIsShown()
        {
            var traditional = UiFonts.Set.FaceFor(TextScript.TraditionalChinese);
            var japanese = UiFonts.Set.FaceFor(TextScript.Japanese);
            var canvas = new GameObject("Canvas", typeof(Canvas));
            try
            {
                var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                text.transform.SetParent(canvas.transform, false);
                text.font = japanese.font;
                text.fontSharedMaterial = japanese.outline;
                text.text = "我們的歌";
                Assert.That(text.textPreprocessor, Is.Null);

                yield return null;
                Assert.That(text.textPreprocessor, Is.Not.Null, "The first draw attaches the face selector.");
                Assert.That(text.font, Is.SameAs(traditional.font));
                Assert.That(text.fontSharedMaterial, Is.SameAs(traditional.outline), "The outline carries over.");
                Assert.That(text.canvasRenderer.GetMaterial(0).mainTexture, Is.EqualTo(traditional.font.atlasTexture),
                    "The renderer draws from the new face's atlas.");
                for (int i = 0; i < text.textInfo.characterCount; i++)
                    Assert.That(text.textInfo.characterInfo[i].fontAsset, Is.SameAs(traditional.font));

                text.text = "さくら";
                yield return null;
                Assert.That(text.font, Is.SameAs(japanese.font));
                Assert.That(text.canvasRenderer.GetMaterial(0).mainTexture, Is.EqualTo(japanese.font.atlasTexture));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(canvas);
            }
        }

        // In English, text of no particular script is drawn in the Latin face (Dom Casual), and text
        // with kana or ideographs stays in its own script's face.
        [UnityTest]
        public IEnumerator TheEnglishInterfaceDrawsLatinTextInTheLatinFace()
        {
            var settings = SettingManager.EnsureInstance().Settings.general;
            string language = settings.language, interfaceLanguage = L10n.Language;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                settings.language = "en";
                L10n.SetLanguage("en");
                Assert.That(UiFonts.LanguageScript, Is.EqualTo(TextScript.Latin));
                var latin = UiFonts.Set.FaceFor(TextScript.Latin);
                var japanese = UiFonts.Set.FaceFor(TextScript.Japanese);

                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var entry = Object.FindAnyObjectByType<EntryScene>();
                var freePlay = entry.Coins.FreePlay;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(freePlay.text, Is.EqualTo(L10n.Get("Menu", "overlay.freePlay")));
                Assert.That(TextScripts.Classify(freePlay.text), Is.EqualTo(TextScript.Neutral));
                Assert.That(freePlay.font, Is.SameAs(latin.font));
                Assert.That(UiFonts.IsOutlined(freePlay), Is.True, "The outline carries over to the Latin face.");

                entry.Don();
                yield return new WaitForSecondsRealtime(4f);
                TestCapture.Capture("EnglishFont.png");

                // Text with kana moves whole to the Japanese face; back to Latin text, back to the Latin face.
                freePlay.text = "フリープレイ";
                yield return null;
                Assert.That(freePlay.font, Is.SameAs(japanese.font));
                freePlay.text = "Free Play";
                yield return null;
                Assert.That(freePlay.font, Is.SameAs(latin.font));

                // In Japanese, the same Latin text goes back to the Japanese face.
                settings.language = "ja";
                L10n.SetLanguage("ja");
                freePlay.text = "Free Play";
                yield return null;
                Assert.That(freePlay.font, Is.SameAs(japanese.font));
            }
            finally
            {
                settings.language = language;
                L10n.SetLanguage(interfaceLanguage);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }
    }
}
