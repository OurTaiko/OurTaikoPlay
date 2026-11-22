using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Each script is drawn in its own UI face and a text never mixes faces (Runtime/Text).
    public sealed class UiFontTests
    {
        static UiFontSet Set => UiFonts.Set;
        static UiFontSet.Face Face(TextScript script) => Set.FaceFor(script);

        [TestCase("さくら", TextScript.Japanese)]
        [TestCase("千本桜", TextScript.Japanese)]
        [TestCase("ドンだー", TextScript.Japanese)]
        [TestCase("國歌と", TextScript.Japanese)]
        [TestCase("我们的歌", TextScript.SimplifiedChinese)]
        [TestCase("张三・李四", TextScript.SimplifiedChinese)]
        [TestCase("我們的歌", TextScript.TraditionalChinese)]
        [TestCase("사랑", TextScript.Neutral)]
        [TestCase("中国", TextScript.Neutral)]
        [TestCase("Hello 123!", TextScript.Neutral)]
        [TestCase("", TextScript.Neutral)]
        public void ClassifiesByCharactersOnlyOneScriptWrites(string text, TextScript expected)
        {
            Assert.That(TextScripts.Classify(text), Is.EqualTo(expected));
        }

        [Test]
        public void EachScriptHasTheFontChosenForIt()
        {
            Assert.That(Face(TextScript.Japanese).font.sourceFontFile.name, Is.EqualTo("大江户勘亭流"));
            Assert.That(Face(TextScript.SimplifiedChinese).font.sourceFontFile.name, Is.EqualTo("方正胖娃GBK"));
            Assert.That(Face(TextScript.TraditionalChinese), Is.SameAs(Face(TextScript.SimplifiedChinese)));
        }

        [Test]
        public void EveryFaceHasAPlainAndAnOutlinedMaterialOnItsOwnAtlas()
        {
            Assert.That(Set.Faces, Is.Not.Empty);
            foreach (var face in Set.Faces)
            {
                Assert.That(face.font, Is.Not.Null);
                Assert.That(face.font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic), face.font.name);
                Assert.That(face.font.material.mainTexture, Is.EqualTo(face.font.atlasTexture), face.font.name);
                Assert.That(face.outline, Is.Not.Null, face.font.name);
                Assert.That(face.outline.mainTexture, Is.EqualTo(face.font.atlasTexture), face.font.name);
                Assert.That(face.outline.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), Is.True, face.font.name);
            }
        }

        [Test]
        public void CoverageComesFromTheSourceFont()
        {
            var simplified = Face(TextScript.SimplifiedChinese);
            Assert.That(simplified.Has('们'), Is.True);
            Assert.That(simplified.Has('♪'), Is.False, "方正胖娃 has no ♪.");
            Assert.That(Face(TextScript.Japanese).Has('♪'), Is.True);
        }

        [Test]
        public void PicksTheFaceOfTheTextsScriptAndTheLanguageForNeutralText()
        {
            Assert.That(Set.Pick("さくら", TextScript.SimplifiedChinese), Is.SameAs(Face(TextScript.Japanese)));
            Assert.That(Set.Pick("我们的歌", TextScript.Japanese), Is.SameAs(Face(TextScript.SimplifiedChinese)));
            Assert.That(Set.Pick("我們的歌", TextScript.Japanese), Is.SameAs(Face(TextScript.TraditionalChinese)));
            Assert.That(Set.Pick("Hello", TextScript.Japanese), Is.SameAs(Face(TextScript.Japanese)));
            Assert.That(Set.Pick("Hello", TextScript.SimplifiedChinese), Is.SameAs(Face(TextScript.SimplifiedChinese)));
        }

        [Test]
        public void MovesTheWholeTextToAnotherFaceWhenItsOwnLacksACharacter()
        {
            const string text = "我们的歌♪";
            var face = Set.Pick(text, TextScript.SimplifiedChinese);
            Assert.That(face, Is.Not.SameAs(Face(TextScript.SimplifiedChinese)));
            foreach (char c in text) Assert.That(face.Has(c), Is.True, c.ToString());
        }

        [Test]
        public void ATextFollowsItsContentAndIsDrawnInOneFace()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            try
            {
                var text = SkinUi.Text("Text", canvas.transform, 40);
                AssertDrawnIn(text, "我们的歌", Face(TextScript.SimplifiedChinese), outlined: true);
                AssertDrawnIn(text, "さくら♪", Face(TextScript.Japanese), outlined: true);
                text.color = Color.black;
                text.UseUiFont();
                AssertDrawnIn(text, "我們的歌", Face(TextScript.TraditionalChinese), outlined: false);
                AssertDrawnIn(text, "我们的歌♪", Set.Pick("我们的歌♪", UiFonts.LanguageScript), outlined: false);
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        static void AssertDrawnIn(TMP_Text text, string content, UiFontSet.Face face, bool outlined)
        {
            text.text = content;
            text.ForceMeshUpdate();
            Assert.That(text.font, Is.SameAs(face.font), content);
            Assert.That(text.fontSharedMaterial, Is.SameAs(outlined ? face.outline : face.font.material), content);
            Assert.That(UiFonts.IsOutlined(text), Is.EqualTo(outlined), content);
            var info = text.textInfo;
            Assert.That(info.characterCount, Is.EqualTo(content.Length), content);
            for (int i = 0; i < info.characterCount; i++)
                Assert.That(info.characterInfo[i].fontAsset, Is.SameAs(face.font), content + " character " + i);
        }
    }
}
