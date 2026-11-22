using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
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
    }
}
