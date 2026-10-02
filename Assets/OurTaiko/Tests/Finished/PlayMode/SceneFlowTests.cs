using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SceneFlowTests
    {
        [UnityTest] public IEnumerator BranchNormalCanBePlayed() => PlayBranch(BranchRoute.Normal);
        [UnityTest] public IEnumerator BranchExpertCanBePlayed() => PlayBranch(BranchRoute.Expert);
        [UnityTest] public IEnumerator BranchMasterAutoPlayCompletes() => PlayBranch(BranchRoute.Master);

        [UnityTest]
        public IEnumerator NijiiroGaugeClearsFillsAndAnimates()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            var canvas = SceneCanvas();
            Assert.That(canvas.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1920, 1080)));
            Assert.That(canvas.transform.Find("Viewport1920x1080").GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(1920, 1080)));
            Assert.That(play.noteSprites[1].rect.size, Is.EqualTo(new Vector2(192, 192)));
            Assert.That(play.title.font.name, Is.EqualTo("Nijiiro SDF"));
            var gauge = play.soulGauge;
            foreach (double threshold in new[] { 0.6, 0.7, 0.8 })
            {
                gauge.Initialize(threshold);
                gauge.SetValue(threshold - 0.001, 0);
                gauge.ShowTime(0.5);
                Assert.That(gauge.IsClear, Is.False);
                Assert.That(gauge.clearCap.enabled, Is.False);
                Assert.That(gauge.clearLabel.sprite.name, Is.EqualTo("clear_dark_ja"));
                gauge.SetValue(threshold, 1);
                gauge.ShowTime(1.225);
                Assert.That(gauge.IsClear, Is.True);
                Assert.That(gauge.cellFade.enabled, Is.True);
                Assert.That(gauge.cellFade.color.a, Is.EqualTo(0.5f).Within(0.001));
                Assert.That(gauge.clearCap.enabled, Is.False);
                gauge.ShowTime(1.451);
                Assert.That(gauge.clearCap.enabled, Is.True);
                Assert.That(gauge.goldTop.enabled, Is.False, "The clear cap must not fill an extra cell.");
                Assert.That(gauge.clearLabel.sprite.name, Is.EqualTo("clear_ja"));
                Assert.That(gauge.red.rectTransform.rect.width, Is.EqualTo(((int)(threshold * 50) - 1) * 21));
            }
            gauge.SetValue(0.86, 2);
            gauge.ShowTime(2.5);
            Assert.That(gauge.goldTop.rectTransform.rect.width, Is.EqualTo(63));
            Assert.That(gauge.goldTop.rectTransform.rect.height + gauge.goldBottom.rectTransform.rect.height, Is.EqualTo(66));
            Assert.That(gauge.red.rectTransform.rect.height, Is.EqualTo(33));
            Assert.That(gauge.cellFade.enabled, Is.False);
            Capture("GaugeClear.png");
            gauge.SetValue(1, 3);
            gauge.ShowTime(3.45);
            Assert.That(gauge.rainbowA.enabled && gauge.fire.enabled, Is.True);
            Assert.That(gauge.rainbowA.color.a, Is.EqualTo(1).Within(0.001));
            Capture("GaugeRainbowA.png");
            gauge.ShowTime(3.60);
            Assert.That(gauge.rainbowA.sprite.name, Is.EqualTo("Rainbowhard0"));
            var first = gauge.rainbowA.sprite;
            gauge.ShowTime(3.7125);
            Assert.That(gauge.rainbowA.sprite, Is.Not.SameAs(first));
            Assert.That(gauge.rainbowB.color.a, Is.EqualTo(0.5f).Within(0.001));
            Capture("GaugeRainbowB.png");
            var frozenFrame = gauge.rainbowA.sprite;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(gauge.rainbowA.sprite, Is.SameAs(frozenFrame), "Pause freezes the gauge animation.");
            gauge.SetValue(0.9, 4);
            Assert.That(gauge.rainbowA.enabled || gauge.rainbowB.enabled || gauge.fire.enabled, Is.False);
            gauge.SetValue(0.4, 5);
            Assert.That(gauge.clearCap.enabled || gauge.goldTop.enabled || gauge.goldBottom.enabled, Is.False);
            Assert.That(gauge.soul.sprite.name, Is.EqualTo("tamashii_dark"));
            gauge.SetValue(1, 6);
            Assert.That(gauge.rainbowA.color.a, Is.Zero, "Refilling restarts the rainbow fade.");
            play.Restart(); yield return WaitForRestart(play);
            var restarted = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(restarted.soulGauge.FilledCells, Is.Zero);
            Assert.That(restarted.soulGauge.rainbowA.enabled, Is.False);
            restarted.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
        }

        static IEnumerator PlayBranch(BranchRoute expected)
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<SongSelectScene>();
            var song = menu.songs.Single(s => s.name == "BranchTraining");
            SceneSwitcher.Instance.Play(song, expected == BranchRoute.Master);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            float deadline = Time.realtimeSinceStartup + 40;
            int hits = 0;
            while (play.Session.BranchHistory.Count == 0)
            {
                if (play.IsPaused) play.TogglePause();
                if (expected == BranchRoute.Expert && hits < 2 && play.SongTime >= hits * 2)
                {
                    play.Hit(false, false); hits++;
                }
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(expected));
            var branch = play.branchLane;
            Assert.That(branch.gameObject.activeInHierarchy, Is.True);
            Assert.That(branch.currentLabel.sprite.name, Is.EqualTo(expected.ToString().ToLowerInvariant()));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(1606.5f, -64.5f)));
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(1));
            Assert.That(branch.background.enabled, Is.EqualTo(expected != BranchRoute.Normal));
            if (expected != BranchRoute.Normal)
            {
                Assert.That(branch.background.sprite.name, Is.EqualTo(expected.ToString().ToLowerInvariant() + "_bg"));
                Assert.That(branch.background.color.a, Is.EqualTo(0.5f));
            }
            Assert.That(branch.transform.GetSiblingIndex(), Is.LessThan(play.noteLayer.parent.GetSiblingIndex()));
            Assert.That(branch.transform.parent.parent.Find("BranchPanel"), Is.Null);
            Assert.That(play.Session.Chart.Notes.Select((n, i) => n.BranchId != 0 || n.Route == expected || play.NoteRoot(i) == null).All(x => x), Is.True);
            Assert.That(play.Session.Chart.Notes.Select((n, i) => n.BranchId == 0 && n.Route == expected && play.NoteRoot(i) != null).Any(x => x), Is.True);
            Assert.That(play.Session.Chart.Bars.Select((n, i) => n.BranchId != 0 || n.Route == expected || play.BarRoot(i) == null).All(x => x), Is.True);
            Capture("Branch" + expected + ".png");
            play.TogglePause(); double pausedAt = play.SongTime;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(play.SongTime, Is.EqualTo(pausedAt).Within(0.001));
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(expected));
            play.TogglePause();
            yield return WaitForResume(play);
            if (expected == BranchRoute.Master)
            {
                while (!play.IsFinished)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                Assert.That(play.Session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
                Assert.That(play.Session.Bad, Is.Zero);
                Assert.That(play.Session.Good, Is.EqualTo(play.Session.Chart.Notes.Count(n => !n.IsLong && play.Session.IsActive(n))));
                yield return WaitForScene(SceneSwitcher.ResultScene);
                Assert.That(SceneSwitcher.Instance.LastResult.AutoPlay && SceneSwitcher.Instance.LastResult.Bad == 0, Is.True);
                Assert.That(Object.FindFirstObjectByType<ResultScene>().Result, Is.SameAs(SceneSwitcher.Instance.LastResult));
            }
            SceneSwitcher.Instance.Restart(); yield return WaitForScene(SceneSwitcher.GameScene);
            var restarted = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(restarted.Session.BranchHistory.Count, Is.Zero);
            Assert.That(restarted.Session.Score, Is.Zero);
            Assert.That(restarted.branchLane.currentLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(restarted.branchLane.background.enabled, Is.False);
            restarted.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
        }

        [UnityTest]
        public IEnumerator TouchDrumMatchesOriginalZonesAndSqueeze()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            // DrumPad is disabled while the pause menu is open, so inspect its live gameplay state.
            if (play.IsPaused) { play.Resume(); yield return WaitForResume(play); }
            var pads = Object.FindObjectsByType<DrumPad>(FindObjectsSortMode.None);
            Assert.That(pads.Length, Is.EqualTo(1));
            var pad = pads[0];
            var zone = (RectTransform)pad.transform;
            var image = pad.drum.GetComponent<UnityEngine.UI.Image>();
            Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(1920, 1080)));
            Assert.That(image.color.a, Is.EqualTo(0.5f));
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(pad.drum.pivot, Is.EqualTo(new Vector2(0.5f, 0)));
            // Design-area fractions measured from the bottom-left corner.
            Vector2 At(float x, float y)
            {
                var area = zone.rect;
                return RectTransformUtility.WorldToScreenPoint(null, zone.TransformPoint(new Vector3(area.xMin + area.width * x, area.yMin + area.height * y)));
            }
            InputKey Hit(Vector2 point) { Assert.That(pad.TryHit(point, out var key), Is.True); return key; }
            Assert.That(Hit(At(0.25f, 0.75f)), Is.EqualTo(InputKey.LeftKa));
            Assert.That(Hit(At(0.75f, 0.75f)), Is.EqualTo(InputKey.RightKa));
            Assert.That(Hit(At(0.45f, 0.05f)), Is.EqualTo(InputKey.LeftDon));
            Assert.That(Hit(At(0.55f, 0.40f)), Is.EqualTo(InputKey.RightDon));
            // Ellipse radii 0.262 / 0.242 of the width: just outside the rim is ka again.
            Assert.That(Hit(At(0.5f - 0.27f, 0.01f)), Is.EqualTo(InputKey.LeftKa));
            Assert.That(Hit(At(0.95f, 0.05f)), Is.EqualTo(InputKey.RightKa));
            Assert.That(Hit(At(0.5f + 0.01f, 0.242f * 1920 / 1080 - 0.01f)), Is.EqualTo(InputKey.RightDon));
            Assert.That(Hit(At(0.5f + 0.01f, 0.242f * 1920 / 1080 + 0.01f)), Is.EqualTo(InputKey.RightKa));
            var pause = (RectTransform)play.pauseButton.transform;
            Assert.That(pad.TryHit(RectTransformUtility.WorldToScreenPoint(null, pause.TransformPoint(pause.rect.center)), out _), Is.False);

            Assert.That(pad.drum.localScale.x, Is.EqualTo(1f));
            pad.Press();
            double start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < 0.035) yield return null;
            yield return null;
            Assert.That(pad.drum.localScale.x, Is.LessThan(1f).And.GreaterThanOrEqualTo(0.95f));
            while (Time.realtimeSinceStartupAsDouble - start < 0.2) yield return null;
            yield return null;
            Assert.That(pad.drum.localScale.x, Is.EqualTo(1f));
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        [UnityTest]
        public IEnumerator BranchLaneTransitionsMatchOriginalSkin()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<SongSelectScene>();
            SceneSwitcher.Instance.Play(menu.songs.Single(s => s.name == "BranchTraining"), true);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            var branch = play.branchLane;
            branch.Select(BranchRoute.Expert, 0);
            branch.ShowTime(0.05);
            Assert.That(branch.previousLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(branch.previousLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-87).Within(0.001));
            Assert.That(branch.currentLabel.color.a, Is.Zero);
            Assert.That(branch.levelChange.sprite.name, Is.EqualTo("level_up"));
            branch.ShowTime(0.1665);
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(0.5f).Within(0.001));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-90.75f).Within(0.001));
            float frozenAlpha = branch.currentLabel.color.a;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(frozenAlpha), "Pause must freeze the branch transition.");
            branch.ShowTime(1.4);
            Assert.That(branch.previousLabel.enabled, Is.False);
            Assert.That(branch.levelChange.enabled, Is.False);
            branch.Select(BranchRoute.Expert, 2);
            Assert.That(branch.levelChange.enabled, Is.False, "Selecting the same route must not replay the animation.");
            branch.Select(BranchRoute.Master, 3);
            branch.ShowTime(4.4);
            Assert.That(branch.background.sprite.name, Is.EqualTo("master_bg"));
            Assert.That(branch.background.color.a, Is.EqualTo(0.5f));
            branch.Select(BranchRoute.Normal, 5);
            branch.ShowTime(5.05);
            Assert.That(branch.levelChange.sprite.name, Is.EqualTo("level_down"));
            Assert.That(branch.previousLabel.sprite.name, Is.EqualTo("master"));
            Assert.That(branch.previousLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-42).Within(0.001));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition.y, Is.EqualTo(40.5f).Within(0.001));
            Assert.That(branch.background.enabled, Is.False);
            branch.ShowTime(6.4);
            Assert.That(branch.currentLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(1606.5f, -64.5f)));
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(1));
            play.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
        }

        [UnityTest]
        public IEnumerator DrumrollBodiesAndTailsUseNijiiroGeometry()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Drumroll Rendering\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n#SCROLL 0.5\n5008,\n6008,\n#SCROLL -0.5\n5008,\n6008,\n#SCROLL 0\n5008,\n#SCROLL 0.01\n6008,\n#SCROLL 1\n50\n#BPMCHANGE 240\n#SCROLL 2\n08,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var captureNames = new[] { "DrumrollSmall", "DrumrollBig", "DrumrollReverseSmall", "DrumrollReverseBig", "DrumrollStopped", "DrumrollShort", "DrumrollTempoChange" };
                for (int i = 0; i < play.Session.Chart.Notes.Count; i++)
                {
                    var note = play.Session.Chart.Notes[i];
                    double time = note.ScrollX < 0 ? note.EndTime - 0.01 : note.Time - 0.25;
                    render.Invoke(play, new object[] { time });
                    Canvas.ForceUpdateCanvases();
                    var root = play.NoteRoot(i);
                    var head = (RectTransform)root.Find("Head");
                    var body = (RectTransform)root.Find("RollBody");
                    var tail = (RectTransform)root.Find("RollTail");
                    bool big = note.Kind == NoteKind.BigRoll;
                    Assert.That(body.GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo(big ? "RollBodyBig" : "RollBodySmall"));
                    Assert.That(body.GetComponent<UnityEngine.UI.Image>().sprite.texture.filterMode, Is.EqualTo(FilterMode.Point), "Atlas sampling must not bleed neighboring body frames into the join.");
                    Assert.That(tail.GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo(big ? "RollTailBig" : "RollTailSmall"));
                    Assert.That(body.GetSiblingIndex(), Is.LessThan(tail.GetSiblingIndex()));
                    Assert.That(tail.GetSiblingIndex(), Is.LessThan(head.GetSiblingIndex()), "The head must cover the body join.");
                    double speed = note.Bpm / 240 * note.ScrollX * (1920 - 618);
                    double length = (note.EndTime - note.Time) * speed;
                    float direction = length < 0 ? -1 : 1;
                    Vector3 TailPosition() => play.noteLayer.InverseTransformPoint(tail.position);
                    Assert.That(body.rect.height, Is.EqualTo(head.rect.height));
                    Assert.That(body.rect.width, Is.EqualTo(System.Math.Abs(length) + 1.5).Within(0.01));
                    Assert.That(body.localScale.x, Is.EqualTo(direction));
                    Assert.That(tail.rect.size, Is.EqualTo(new Vector2(big ? 120 : 80, 192)));
                    Assert.That(tail.localScale.x, Is.EqualTo(direction));
                    Assert.That(TailPosition().x, Is.EqualTo(120 + (note.EndTime - time) * speed).Within(0.01));
                    Assert.That(TailPosition().y, Is.EqualTo(root.anchoredPosition.y).Within(0.01));
                    var bodyEnd = play.noteLayer.InverseTransformPoint(body.TransformPoint(new Vector3(body.rect.xMax, 0)));
                    Assert.That((bodyEnd.x - TailPosition().x) * direction, Is.EqualTo(1.5).Within(0.01), "Body and cap overlap without a gap.");
                    foreach (Transform child in play.noteLayer) child.gameObject.SetActive(child == root);
                    play.barLayer.gameObject.SetActive(false);
                    Capture(captureNames[i] + ".png");
                    if (i <= 1) Capture(captureNames[i] + "-720p.png", 1280, 720);
                    float headBefore = root.anchoredPosition.x, tailBefore = TailPosition().x;
                    render.Invoke(play, new object[] { time + 0.125 });
                    Assert.That(headBefore - root.anchoredPosition.x, Is.EqualTo(speed * 0.125).Within(0.01));
                    Assert.That(tailBefore - TailPosition().x, Is.EqualTo(speed * 0.125).Within(0.01), "Tail must inherit the head's speed.");
                    Assert.That(tail.anchoredPosition.x, Is.EqualTo(length).Within(0.01));
                }
                play.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator FinishedRollsAndMissedNotesKeepScrollingPastJudge()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Drumroll Exit\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n5008,\n1,\n0,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var note = play.Session.Chart.Notes[0];
                render.Invoke(play, new object[] { 0.5 });
                var root = play.NoteRoot(0).gameObject;
                // draw_notes iterates in reverse: the earlier roll paints over the later don.
                Assert.That(play.NoteRoot(0).GetSiblingIndex(), Is.GreaterThan(play.NoteRoot(1).GetSiblingIndex()));
                var tail = (RectTransform)root.transform.Find("RollTail");
                double speed = note.Bpm / 240 * note.ScrollX * (1920 - 618);
                // Original keeps the roll in draw_note_buffer until the tail's unload_ms,
                // regardless of the roll having been judged at its end time.
                play.Session.Resolved[0] = true;
                render.Invoke(play, new object[] { note.EndTime + 0.1 });
                Assert.That(play.NoteRoot(0), Is.SameAs(root.transform), "A finished roll must flow past the judge instead of vanishing.");
                Assert.That(play.noteLayer.InverseTransformPoint(tail.position).x, Is.EqualTo(120 - 0.1 * speed).Within(0.01));
                render.Invoke(play, new object[] { note.EndTime + 2 });
                Assert.That(play.NoteRoot(0), Is.Null, "The roll unloads once its tail has left the lane.");
                Assert.That(root.activeSelf, Is.False, "An unloaded view returns to the pool inactive.");
                // A missed normal note flows on as well; a hit one is removed.
                var donNote = play.Session.Chart.Notes[1];
                play.Session.Resolved[1] = true;
                render.Invoke(play, new object[] { donNote.Time + 0.2 });
                Assert.That(play.NoteRoot(1), Is.Null, "A hit note leaves the lane at once.");
                play.Session.Missed[1] = true;
                render.Invoke(play, new object[] { donNote.Time + 0.2 });
                Assert.That(play.NoteRoot(1), Is.Not.Null, "A missed note must flow past the judge.");
                var don = play.NoteRoot(1).gameObject;
                Assert.That(((RectTransform)don.transform).anchoredPosition.x, Is.EqualTo(120 - 0.2 * speed).Within(0.01));
                // Culling follows the live lane clip and note size, not fixed pixels:
                // the note stays until its right edge passes the lane's left edge.
                float halfWidth = ((RectTransform)don.transform).rect.width / 2;
                double exit = donNote.Time + (120 + halfWidth) / speed;
                render.Invoke(play, new object[] { exit - 0.005 });
                Assert.That(play.NoteRoot(1), Is.Not.Null);
                render.Invoke(play, new object[] { exit + 0.005 });
                Assert.That(play.NoteRoot(1), Is.Null);
                play.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator NijiiroBalloonCounterCountsPopsAndResets()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Balloon Counter\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:12,3\n#START\n7008,\n7008,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                float deadline = Time.realtimeSinceStartup + 20;
                IEnumerator Reach(double time)
                {
                    while (play.RenderedTime < time)
                    {
                        if (play.IsPaused) play.TogglePause();
                        yield return null;
                        Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    }
                }
                var counter = play.balloonCounter;
                Assert.That(play.balloonPop, Is.Not.Null);
                Assert.That(play.balloonPop.name, Is.EqualTo("balloon_pop"));
                Assert.That(play.balloonPop.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                Assert.That(counter.IsVisible, Is.False);
                yield return Reach(0.1);
                play.Hit(true, false);
                Assert.That(counter.IsVisible, Is.False, "Ka must not start a balloon counter.");
                play.Hit(false, false); play.Hit(false, true);
                Assert.That(counter.Remaining, Is.EqualTo(10));
                Assert.That(counter.IsVisible, Is.True);
                Assert.That(counter.number.GetChild(0).GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo("BalloonDigit1"));
                Assert.That(counter.number.GetChild(1).GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo("BalloonDigit0"));
                play.TogglePause(); play.pausePanel.SetActive(false);
                Capture("BalloonCounter10.png");
                var frozenSize = ((RectTransform)counter.number.GetChild(0)).sizeDelta;
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(((RectTransform)counter.number.GetChild(0)).sizeDelta, Is.EqualTo(frozenSize));

                // Deterministic samples of the reference's digit spacing, stretch and pop fade.
                counter.RecordHit(0, 1000, 1, 20, 10);
                Assert.That(counter.Remaining, Is.EqualTo(999));
                Assert.That(counter.number.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.EqualTo(3));
                Assert.That(((RectTransform)counter.number.GetChild(0)).anchoredPosition.x, Is.EqualTo(-96));
                counter.ShowTime(10.0255);
                Assert.That(((RectTransform)counter.number.GetChild(0)).rect.height, Is.EqualTo(97).Within(0.001));
                counter.ShowTime(10.2);
                Assert.That(((RectTransform)counter.number.GetChild(0)).rect.height, Is.EqualTo(90));
                counter.RecordHit(0, 1000, 1000, 20, 11);
                counter.ShowTime(11.083);
                Assert.That(counter.visuals.alpha, Is.EqualTo(0.5f).Within(0.001));
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(counter.visuals.alpha, Is.EqualTo(0.5f).Within(0.001), "Pause freezes the pop fade.");
                counter.ShowTime(11.167);
                Assert.That(counter.IsVisible, Is.False);
                counter.RecordHit(0, 12, 2, play.Session.Chart.Notes[0].EndTime, play.SongTime);
                play.TogglePause();
                yield return WaitForResume(play);

                play.Hit(false, false);
                Assert.That(counter.Remaining, Is.EqualTo(9));
                Assert.That(counter.number.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.EqualTo(1));
                Assert.That(counter.body.sprite.name, Is.EqualTo("BalloonInflation2"));
                play.TogglePause(); play.pausePanel.SetActive(false);
                Capture("BalloonCounter9.png", 1280, 720);
                play.TogglePause();
                yield return WaitForResume(play);
                for (int i = 0; i < 9; i++) play.Hit(false, (i & 1) != 0);
                Assert.That(play.Session.Resolved[0], Is.True);
                Assert.That(counter.Remaining, Is.Zero);
                Assert.That(counter.body.sprite.name, Is.EqualTo("BalloonInflation7"));
                yield return Reach(1);
                Assert.That(counter.IsVisible, Is.False);
                yield return Reach(2.1);
                play.Hit(false, false);
                Assert.That(counter.Remaining, Is.EqualTo(2), "Each balloon starts with its own required hits.");
                yield return Reach(3.51);
                Assert.That(counter.IsVisible, Is.False, "An unpopped balloon clears at its end time.");
                play.Restart(); yield return WaitForRestart(play);
                play = Object.FindFirstObjectByType<PlayScene>();
                Assert.That(play.balloonCounter.IsVisible, Is.False);
                Assert.That(play.Session.LongHits[0], Is.Zero);
                SceneSwitcher.Instance.Play(song, true); yield return WaitForScene(SceneSwitcher.GameScene);
                play = Object.FindFirstObjectByType<PlayScene>();
                yield return Reach(0.1);
                Assert.That(play.balloonCounter.Remaining, Is.EqualTo(12 - play.Session.LongHits[0]));
                Assert.That(play.balloonCounter.IsVisible, Is.True);
                yield return Reach(1);
                Assert.That(play.Session.Resolved[0], Is.True);
                Assert.That(play.balloonCounter.IsVisible, Is.False);
                play.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator BalloonFaceAlignsWithJudgeWhileMovingAndHitting()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Balloon Alignment\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:3,3\n#START\n7008,\n9008,\n1000,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                float deadline = Time.realtimeSinceStartup + 8;
                while (play.RenderedTime < -0.5 || play.NoteRoot(0) == null)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                var balloon = play.NoteRoot(0);
                var head = (RectTransform)balloon.Find("Head");
                var circle = (RectTransform)play.noteLayer.parent.parent.Find("JudgeCircle");
                Vector3 judge = play.noteLayer.InverseTransformPoint(circle.TransformPoint(circle.rect.center));
                // The face center in Nijiiro's 192px balloon crop is (114, 96),
                // unlike the centered face in the normal-note and kusudama crops.
                Vector3 FaceWorldPosition() => head.TransformPoint(new Vector3(head.rect.width * (114f / 192f - 0.5f), 0));
                Vector3 FacePosition() => play.noteLayer.InverseTransformPoint(FaceWorldPosition());
                Assert.That(play.RenderedTime, Is.LessThan(0));
                play.Hit(false, false);
                Assert.That(play.Session.LongHits[0], Is.Zero, "The visual offset must not start balloon hits early.");
                Capture("BalloonApproaching.png");
                double distance = NoteScroll.DistanceFromJudge(0, play.RenderedTime, 120, 1, 1920 - 618);
                Assert.That(FacePosition().x, Is.EqualTo(judge.x + distance).Within(0.01));
                Assert.That(FacePosition().y, Is.EqualTo(judge.y).Within(0.01));
                while (play.RenderedTime < 0.15)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                play.Hit(false, false);
                Assert.That(play.Session.LongHits[0], Is.EqualTo(1));
                Assert.That(Vector3.Distance(FacePosition(), judge), Is.LessThan(0.01), "The face must stay centered on the judge during hits.");
                play.TogglePause(); play.pausePanel.SetActive(false);
                foreach (int noteSize in new[] { 96, 288, 192 })
                {
                    balloon.sizeDelta = new Vector2(noteSize, noteSize);
                    Canvas.ForceUpdateCanvases();
                    Assert.That(head.rect.width, Is.EqualTo(noteSize));
                    Assert.That(Vector3.Distance(FacePosition(), judge), Is.LessThan(0.01), "Resizing the note must preserve face alignment.");
                }
                foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1280, 1024) })
                {
                    Capture($"BalloonAtJudge-{resolution.x}x{resolution.y}.png", resolution.x, resolution.y, camera =>
                    {
                        var canvas = head.GetComponentInParent<Canvas>();
                        float expectedScale = Mathf.Min(resolution.x / 1920f, resolution.y / 1080f);
                        Assert.That(canvas.scaleFactor, Is.EqualTo(expectedScale).Within(0.001));
                        var faceOnScreen = camera.WorldToScreenPoint(FaceWorldPosition());
                        var judgeOnScreen = camera.WorldToScreenPoint(circle.TransformPoint(circle.rect.center));
                        Assert.That(Vector3.Distance(faceOnScreen, judgeOnScreen), Is.LessThan(0.01), $"Face alignment at {resolution}.");
                    });
                }
                play.TogglePause();
                yield return WaitForResume(play);
                play.Hit(false, true);
                play.Hit(false, false);
                yield return null;
                Assert.That(play.Session.Resolved[0], Is.True);
                Assert.That(play.NoteRoot(0), Is.Null);
                Assert.That(balloon.gameObject.activeSelf, Is.False);
                play.TogglePause(); play.pausePanel.SetActive(false);
                typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(play, new object[] { 3.0 });
                Canvas.ForceUpdateCanvases();
                foreach (int index in new[] { 1, 2 })
                {
                    var otherNote = play.NoteRoot(index);
                    var otherHead = (RectTransform)otherNote.Find("Head");
                    Assert.That(Vector3.Distance(otherHead.TransformPoint(otherHead.rect.center), otherNote.TransformPoint(otherNote.rect.center)),
                        Is.LessThan(0.01), "Kusudama and normal notes must keep their centered artwork.");
                }
                play.Back(); yield return WaitForScene(SceneSwitcher.SongSelectScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart);
                Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator RenderedNoteTravelsAtOriginalSkinSpeed()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            float deadline = Time.realtimeSinceStartup + 5;
            do
            {
                if (play.IsPaused) play.TogglePause();
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            } while (play.NoteRoot(0) == null || play.RenderedTime < -1);
            var rect = play.NoteRoot(0);
            // DSP time can advance by an audio block after Update; sample the timestamp
            // actually used by the renderer together with its matching position.
            double timeBefore = play.RenderedTime;
            float xBefore = rect.anchoredPosition.x;
            yield return new WaitForSecondsRealtime(0.15f);
            double elapsed = play.RenderedTime - timeBefore;
            Assert.That(elapsed, Is.GreaterThan(0));
            // Nijiiro skin: screen width 1920, judge x 618; default song starts at BPM 160 / SCROLL 1.
            double expectedDistance = elapsed * 160 / 240 * (1920 - 618);
            Assert.That(xBefore - rect.anchoredPosition.x, Is.EqualTo(expectedDistance).Within(0.01));
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator SongSelectPlayPauseResumeRestartAndReturn()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(menu, Is.Not.Null, "SongSelect is the menu scene.");
            SceneSwitcher.Instance.Play(menu.songs[0], true);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session.Chart.Notes.Count, Is.GreaterThan(50));
            Assert.That(play.branchLane.gameObject.activeSelf, Is.False, "Non-branch charts must not show route labels or tints.");
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            if (play.IsPaused) { play.Resume(); yield return WaitForResume(play); }
            yield return new WaitForSecondsRealtime(2.4f);
            Assert.That(play.Session.Good, Is.GreaterThan(0));
            Assert.That(play.music.isPlaying, Is.True);
            Assert.That(play.music.time, Is.EqualTo(play.SongTime).Within(0.15));
            play.TogglePause(); double pausedAt = play.SongTime;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(play.SongTime, Is.EqualTo(pausedAt).Within(0.001));
            Assert.That(play.music.isPlaying, Is.False);
            play.TogglePause();
            yield return WaitForResume(play);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(play.music.time, Is.EqualTo(play.SongTime).Within(0.15));
            Capture("SinglePlayScene.png");
            play.Restart();
            yield return WaitForRestart(play);
            var restarted = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(restarted, Is.Not.SameAs(play));
            Assert.That(restarted.Session.Score, Is.Zero);
            Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            restarted.Back();
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(Object.FindFirstObjectByType<PlayScene>(), Is.Null);
            Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SinglePlaySceneCanBeOpenedDirectly()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 2;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session, Is.Not.Null);
            Assert.That(SceneSwitcher.Instance, Is.Not.Null);
            Assert.That(Application.targetFrameRate, Is.EqualTo(120));
            Assert.That(QualitySettings.vSyncCount, Is.Zero);
            Assert.That(UnityEngine.Rendering.OnDemandRendering.renderFrameInterval, Is.EqualTo(1));
            var fps = Object.FindFirstObjectByType<FpsCounter>();
            Assert.That(fps, Is.Not.Null);
            if (!play.IsPaused) play.TogglePause();
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(fps.FramesPerSecond, Is.GreaterThan(0));
                var label = fps.GetComponent<TMPro.TMP_Text>();
                Assert.That(label.text, Does.StartWith("FPS ").And.Not.Contains("--"));
                Assert.That(label.raycastTarget, Is.False);
            }
            finally { Time.timeScale = originalTimeScale; }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            Assert.That(Object.FindObjectsByType<FpsCounter>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }

        static IEnumerator WaitForResume(PlayScene play)
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (play.IsPaused)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The pause menu did not finish fading out.");
                yield return null;
            }
        }

        static IEnumerator WaitForRestart(PlayScene oldPlay)
        {
            // A paused restart first fades its menu; the scene name is unchanged during that wait.
            float deadline = Time.realtimeSinceStartup + 20;
            while (oldPlay != null)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Restart did not replace the play scene.");
                yield return null;
            }
            yield return WaitForScene(SceneSwitcher.GameScene);
        }

        static Canvas SceneCanvas() => TestCapture.SceneCanvas();

        internal static void Capture(string name, int width = 1920, int height = 1080, System.Action<Camera> verify = null)
            => TestCapture.Capture(name, width, height, verify);
    }
}
