using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class EndingClipTests
    {
        [TestCase("fail", "EndingFail", false, 1, 1, 180)]
        [TestCase("clear", "EndingClear", true, 1, 1, 180)]
        [TestCase("fullcombo", "EndingFullCombo", true, 1, 0, 325)]
        [TestCase("donderful", "EndingDonderful", true, 0, 0, 325)]
        public void SavedClipsReproduceEverySourceFrame(string kind, string clipName, bool clear, int ok, int bad, int count)
        {
            var data = JObject.Parse(File.ReadAllText("Assets/OurTaiko/Editor/EndingTimelines/" + kind + ".json"));
            var shapes = data["shapes"].ToDictionary(s => (int)s["id"]);
            foreach (var shape in shapes.Values)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/OurTaiko/Art/game/ending/" + (string)shape["sprite"] + ".png");
                Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, (float)shape["w"], (float)shape["h"])),
                    "Preserve transparent margins: trimmed sprites get stretched to the original movie dimensions.");
                Assert.That(sprite.vertices.Length, Is.EqualTo(4), "Each exported leaf is a complete rectangular image.");
            }
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OurTaiko/Generated/Ending.prefab"));
            try
            {
                var view = root.GetComponent<EndingView>(); view.audioSource = null;
                view.Begin(new PlayResult { IsClear = clear, Ok = ok, Bad = bad });
                var sampler = root.GetComponent<ClipSampler>();
                Assert.That(sampler.clip.name, Is.EqualTo(clipName));
                Assert.That(view.Duration, Is.EqualTo(count / 60.0).Within(1e-5));
                Assert.That(sampler.clip.isLooping, Is.False);
                for (int f = 0; f < count; f++)
                {
                    view.ShowTime((f + .5) / 60);
                    var rows = data["frames"][f]["rows"];
                    for (int i = 0; i < root.transform.childCount; i++)
                    {
                        var image = root.transform.GetChild(i).GetComponent<Image>();
                        Assert.That(image.enabled, Is.EqualTo(i < rows.Count()), $"{kind} frame {f} slot {i}");
                        Assert.That(image.raycastTarget, Is.False);
                        if (!image.enabled) continue;
                        var r = rows[i]["values"]; var s = shapes[(int)r[0]];
                        Assert.That(AssetDatabase.GetAssetPath(image.sprite), Is.EqualTo("Assets/OurTaiko/Art/game/ending/" + (string)s["sprite"] + ".png"));
                        Assert.That(image.color.a, Is.EqualTo((float)r[6]).Within(.001));
                        Assert.That(image.rectTransform.anchoredPosition.x, Is.EqualTo((float)r[1]).Within(.01));
                        Assert.That(image.rectTransform.anchoredPosition.y, Is.EqualTo(-(float)r[2]).Within(.01));
                        Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo((float)s["w"]).Within(.01));
                        Assert.That(image.rectTransform.pivot.y, Is.EqualTo(1 - (float)s["oy"] / (float)s["h"]).Within(.001));
                        Assert.That(image.rectTransform.localScale.x, Is.EqualTo((float)r[3]).Within(.001));
                        Assert.That(image.rectTransform.localScale.y, Is.EqualTo((float)r[4]).Within(.001));
                        Assert.That(Mathf.DeltaAngle(image.rectTransform.localEulerAngles.z, -(float)r[5]), Is.EqualTo(0).Within(.01));
                        bool add = r.Count() > 7 && (int)r[7] == 8;
                        Assert.That(image.material.name == "UI Additive", Is.EqualTo(add));
                    }
                }
                view.ShowTime(100);
                Assert.That(root.transform.GetComponentsInChildren<Image>().Count(i => i.enabled),
                    Is.EqualTo(data["frames"].Last["rows"].Count()), "Hold the last frame during the curtain.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FailedGaugeCannotShowAComboEndingAndVariantsResetLayers()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OurTaiko/Generated/Ending.prefab"));
            try
            {
                var view = root.GetComponent<EndingView>(); view.audioSource = null;
                view.Begin(new PlayResult { IsClear = true }); view.ShowTime(2);
                view.Begin(new PlayResult { IsClear = false, Bad = 0, Ok = 0 });
                Assert.That(root.GetComponent<ClipSampler>().clip, Is.SameAs(view.fail));
                Assert.That(root.GetComponentsInChildren<Image>().All(i => !i.enabled), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
