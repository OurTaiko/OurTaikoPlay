using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Collections.Generic;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class SongSearchFlowTests
    {
        FanmadeFixture first, second;
        string root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("SearchTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            first?.Dispose(); second?.Dispose(); TestData.UseServers(new ServerList());
            TestSongs.Install();
            if (root != null) Directory.Delete(root, true);
        }

        [UnityTest]
        public IEnumerator SearchesLocalFoldersAndAllServersThenRestoresBrowsing()
        {
            first = new FanmadeFixture(); second = new FanmadeFixture();
            first.Charts.Add(new FanmadeFixture.Chart { Title = "Keyword online A" });
            // Uncategorized songs are absent from bootstrap's categories but still searchable/playable.
            second.Charts.Add(new FanmadeFixture.Chart { Title = "Keyword online B", Categories = Array.Empty<string>() });
            TestData.UseServers(new ServerList { servers = { first.Server(), second.Server() } });
            var online = OnlineManager.Instance;
            var connect = System.Threading.Tasks.Task.WhenAll(online.Client.ConnectAsync(online.Client.Add(first.Server()), true), online.Client.ConnectAsync(online.Client.Add(second.Server()), true));
            yield return Await(connect); online.RefreshSongs();
            root = Path.Combine(Application.temporaryCachePath, "search-songs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Folder"));
            File.WriteAllText(Path.Combine(root, "Folder", "box.def"), "#TITLE:Folder\n#GENRE:GAME\n");
            File.WriteAllText(Path.Combine(root, "Folder", "song.tja"), "TITLE:Keyword local\nBPM:120\nCOURSE:Oni\nLEVEL:8\n#START\n1000,\n#END");
            LocalSongLibrary.EnsureInstance().UseRoot(root);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
            var scene = Object.FindFirstObjectByType<SongSelectScene>(); var manager = scene.Manager; var view = scene.view.search;
            Assert.That(view, Is.Not.Null); int initial = manager.BoardCount;
            Assert.That(view.transform.Find("OpenSearch"), Is.Null, "The standalone entry button has been removed.");
            var searchEntry = manager.Items.Single(i => i.Kind == SongSelectManager.ItemKind.Search);
            manager.SelectItem(searchEntry); yield return new WaitForSeconds(.85f);
            Assert.That(manager.FocusedItem, Is.SameAs(searchEntry));
            TestCapture.Capture("SongSearchFolderEntry.png");
            manager.SelectItem(searchEntry); yield return null;
            Assert.That(manager.SearchDialogOpen, Is.True);
            Assert.That(view.FocusedRow, Is.Zero);
            Assert.That(view.keyword.isFocused, Is.False);
            var oldFocus = manager.Focused; manager.Right(); Assert.That(manager.Focused, Is.EqualTo(oldFocus));
            Canvas.ForceUpdateCanvases();
            foreach (var control in new Component[] { view.keyword, view.next[0], view.apply, view.close })
            {
                var rect = (RectTransform)control.transform;
                var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(hits[0].gameObject.transform.IsChildOf(control.transform), Is.True, control.name + " must receive pointer input.");
            }
            TestCapture.Capture("SongSearchPlaceholder.png");
            view.keyword.text = "Keyword";
            TestCapture.Capture("SongSearchDialog.png");
            view.Submit();
            float end = Time.realtimeSinceStartup + 15;
            while (view.IsOpen && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(view.IsOpen, Is.False); Assert.That(manager.SearchCount, Is.EqualTo(3));
            Assert.That(manager.Items.Count(i => i.Kind == SongSelectManager.ItemKind.Song), Is.EqualTo(3));
            Assert.That(manager.Items.Where(i => i.Song != null && i.Song.onlineChart != null).All(i => online.IsOnline(i.Song)), Is.True);
            yield return new WaitForSeconds(.5f);
            TestCapture.Capture("SongSearchResults.png");
            var selected = manager.FocusedSong;
            manager.Confirm(); Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            // A scene reload represents returning from play and keeps the result selection.
            SceneSwitcher.Instance.Select(selected);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
            scene = Object.FindFirstObjectByType<SongSelectScene>(); manager = scene.Manager; view = scene.view.search;
            Assert.That(manager.SearchActive, Is.True); Assert.That(manager.FocusedSong, Is.SameAs(selected));
            searchEntry = manager.Items.Single(i => i.Kind == SongSelectManager.ItemKind.Search);
            manager.SelectItem(searchEntry); manager.SelectItem(searchEntry); yield return null;
            Assert.That(view.IsOpen, Is.True, "Results also contain the search folder for editing the query.");
            view.Clear(); yield return null;
            Assert.That(manager.FocusedKind, Is.EqualTo(SongSelectManager.ItemKind.Search));
            Assert.That(manager.SearchActive, Is.False); Assert.That(manager.BoardCount, Is.EqualTo(initial));
            Assert.That(manager.SearchDialogOpen, Is.False);
            yield return Await(manager.SearchAsync(new SongSearchQuery("nothing matches"), CancellationToken.None));
            Assert.That(manager.SearchCount, Is.Zero); Assert.That(manager.BoardCount, Is.EqualTo(2));
            manager.Navigate(1); manager.Confirm(); Assert.That(manager.SearchActive, Is.False);
            File.AppendAllText(Path.Combine(root, "Folder", "song.tja"), "\nCOURSE:Edit\nLEVEL:8\n#START\n1000,\n#END\n");
            LocalSongLibrary.Instance.Refresh();
            yield return Await(manager.SearchAsync(new SongSearchQuery("Keyword", Difficulty.Ura, 8), CancellationToken.None));
            Assert.That(manager.SearchCount, Is.EqualTo(1));
            manager.Confirm();
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Ura), "A Ura search must open the Ura side of the Oni card.");
        }
        [UnityTest]
        public IEnumerator EditingCancelsAnOldRequestAndKeyboardFocusDoesNotMoveTheWheel()
        {
            first = new FanmadeFixture();
            first.Charts.Add(new FanmadeFixture.Chart { Title = "Fresh song" });
            TestData.UseServers(new ServerList { servers = { first.Server() } });
            var online = OnlineManager.Instance;
            yield return Await(online.Client.ConnectAsync(online.Client.Add(first.Server()), true));
            online.RefreshSongs();
            var originalBackground = InputSystem.settings.backgroundBehavior;
            var originalEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>("SearchKeyboard"); keyboard.MakeCurrent();
            using var pending = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            SongSelectManager observedManager = null;
            var sounds = new List<SongSelectManager.Sound>();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
                var scene = Object.FindFirstObjectByType<SongSelectScene>(); var view = scene.view.search; var manager = scene.Manager;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.That(view.IsOpen, Is.False, "Tab no longer opens search from the wheel.");
                var entry = manager.Items.Single(i => i.Kind == SongSelectManager.ItemKind.Search);
                manager.SelectItem(entry);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.That(view.IsOpen, Is.True); Assert.That(view.keyword.isFocused, Is.False);
                Assert.That(view.FocusedRow, Is.Zero, "Opening search selects Difficulty without activating the keyboard.");
                observedManager = manager; manager.SoundRequested += sounds.Add;
                yield return Press(keyboard, Key.D);
                yield return Press(keyboard, Key.K);
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Ka, SongSelectManager.Sound.Ka }));
                sounds.Clear();
                for (int i = 0; i < 3; i++) yield return Press(keyboard, Key.F);
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Don, SongSelectManager.Sound.Don, SongSelectManager.Sound.Don }));
                Assert.That(view.FocusedRow, Is.EqualTo(3)); Assert.That(view.EditingKeyword, Is.False);
                sounds.Clear();
                yield return Press(keyboard, Key.K);
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Ka }), "Entering text mode plays one Ka."); sounds.Clear();
                Assert.That(view.EditingKeyword, Is.True); Assert.That(view.keyword.isFocused, Is.True);
                int focus = manager.Focused;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.D, Key.J, Key.K)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.That(manager.Focused, Is.EqualTo(focus)); Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.Browsing));
                foreach (var key in new[] { Key.Tab, Key.UpArrow, Key.DownArrow, Key.Escape }) yield return Press(keyboard, key);
                view.next[0].onClick.Invoke(); view.apply.onClick.Invoke(); view.close.onClick.Invoke(); view.clear.onClick.Invoke();
                Assert.That(view.IsOpen, Is.True); Assert.That(view.EditingKeyword, Is.True);
                Assert.That(view.FocusedRow, Is.EqualTo(3)); Assert.That(view.keyword.isFocused, Is.True);
                Assert.That(manager.SearchActive, Is.False);
                view.keyword.text = "Typed keyword";
                yield return Press(keyboard, Key.Enter);
                Assert.That(view.EditingKeyword, Is.False); Assert.That(view.keyword.isFocused, Is.False);
                Assert.That(view.IsOpen, Is.True); Assert.That(view.FocusedRow, Is.EqualTo(3));
                Assert.That(view.keyword.text, Is.EqualTo("Typed keyword")); Assert.That(manager.SearchActive, Is.False, "Enter finishes typing without searching.");
                Assert.That(sounds, Is.Empty, "Typing drum keys and confirming text must not play menu sounds.");
                first.CustomRequest = context =>
                {
                    if (context.Request.Url.AbsolutePath == "/api/v1/game/search" && context.Request.QueryString["q"] == "Old")
                    { pending.Set(); release.Wait(5000); }
                    return false;
                };
                view.keyword.text = "Old"; view.Submit();
                float end = Time.realtimeSinceStartup + 10;
                while (!pending.IsSet && Time.realtimeSinceStartup < end) yield return null;
                Assert.That(pending.IsSet, Is.True);
                view.keyword.text = "Fresh"; view.Submit();
                end = Time.realtimeSinceStartup + 10;
                while (view.IsOpen && Time.realtimeSinceStartup < end) yield return null;
                Assert.That(view.IsOpen, Is.False); Assert.That(manager.SearchCount, Is.EqualTo(1));
                release.Set(); yield return null; yield return null;
                Assert.That(manager.SearchQuery.Keyword, Is.EqualTo("Fresh")); Assert.That(manager.SearchCount, Is.EqualTo(1));
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Don, SongSelectManager.Sound.Don }), "Each submitted search plays once; completion must not play another Don.");
                sounds.Clear(); view.Open(); yield return null;
                view.next[0].onClick.Invoke();
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Ka })); sounds.Clear();
                view.clear.onClick.Invoke(); yield return null;
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Don }), "Clear and close play only one Don together.");
                sounds.Clear(); view.Open(); yield return null; view.close.onClick.Invoke();
                Assert.That(sounds, Is.EqualTo(new[] { SongSelectManager.Sound.Don }));
            }
            finally
            {
                release.Set(); InputSystem.RemoveDevice(keyboard);
                if (observedManager != null) observedManager.SoundRequested -= sounds.Add;
                InputSystem.settings.backgroundBehavior = originalBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = originalEditor;
            }
        }

        [UnityTest]
        public IEnumerator KeywordNeedsASecondPointerClickAndDoneKeepsTheDialogOpen()
        {
            TestData.UseServers(new ServerList()); TestSongs.Install();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
            var scene = Object.FindFirstObjectByType<SongSelectScene>(); var view = scene.view.search;
            view.Open(); yield return null;
            ClickKeyword(view); yield return null;
            Assert.That(view.FocusedRow, Is.EqualTo(3)); Assert.That(view.EditingKeyword, Is.False);
            Assert.That(view.keyword.isFocused, Is.False, "The first touch only selects Keyword.");
            ClickKeyword(view); yield return null;
            Assert.That(view.EditingKeyword, Is.True); Assert.That(view.keyword.isFocused, Is.True);
            view.keyword.text = "Keep this";
            // Touch keyboards dispatch TMP submit when their Done key is pressed.
            view.keyword.onSubmit.Invoke(view.keyword.text); yield return null;
            Assert.That(view.EditingKeyword, Is.False); Assert.That(view.IsOpen, Is.True);
            Assert.That(scene.Manager.SearchActive, Is.False); Assert.That(view.keyword.text, Is.EqualTo("Keep this"));
            ClickKeyword(view); yield return null;
            Assert.That(view.EditingKeyword, Is.True, "A selected Keyword row can be touched to edit again.");
            // Deselecting the field is not confirmation; keep input mode until Done.
            EventSystem.current.SetSelectedGameObject(null); yield return null; yield return null;
            Assert.That(view.EditingKeyword, Is.True); Assert.That(view.keyword.isFocused, Is.True);
            view.keyword.onSubmit.Invoke(view.keyword.text); yield return null;
            view.Close(); yield return null;
            view.Open(); yield return null;
            Assert.That(view.FocusedRow, Is.Zero); Assert.That(view.EditingKeyword, Is.False);
        }

        [UnityTest]
        public IEnumerator DismissingTheTouchKeyboardKeepsTextAndDoesNotReactivateIt()
        {
            TestData.UseServers(new ServerList()); TestSongs.Install();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
            var scene = Object.FindFirstObjectByType<SongSelectScene>(); var view = scene.view.search;
            view.Open(); yield return null;
            ClickKeyword(view); yield return null;
            foreach (var status in new[] { TouchScreenKeyboard.Status.Canceled, TouchScreenKeyboard.Status.LostFocus, TouchScreenKeyboard.Status.Done })
            {
                ClickKeyword(view); yield return null; yield return null;
                Assert.That(view.EditingKeyword, Is.True, "Explicitly tapping Keyword should start a new input session.");
                view.keyword.text = "保留关键词";
                view.keyword.onTouchScreenKeyboardStatusChanged.Invoke(TouchScreenKeyboard.Status.Visible);
                Assert.That(view.EditingKeyword, Is.True);
                // Emulate the native status event; desktop Editor cannot open a mobile keyboard.
                view.keyword.onTouchScreenKeyboardStatusChanged.Invoke(status);
                view.Close(); view.Submit(); view.clear.onClick.Invoke();
                Assert.That(view.IsOpen, Is.True, "The dismissal gesture must not also operate the menu.");
                yield return null; yield return null;
                Assert.That(view.EditingKeyword, Is.False, status.ToString());
                Assert.That(view.keyword.isFocused, Is.False, "Subsequent ticks must not reopen the keyboard.");
                Assert.That(view.keyword.enabled, Is.False);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
                Assert.That(view.keyword.text, Is.EqualTo("保留关键词"));
                Assert.That(view.apply.interactable && view.clear.interactable && view.close.interactable, Is.True);
                Assert.That(scene.Manager.SearchActive, Is.False);
            }
            view.Submit();
            float end = Time.realtimeSinceStartup + 15;
            while (view.IsOpen && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(view.IsOpen, Is.False);
            Assert.That(scene.Manager.SearchQuery.Keyword, Is.EqualTo("保留关键词"));
        }

        static IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }

        static void ClickKeyword(SongSearchView view)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)view.keyword.transform;
            var data = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0)); data.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        static IEnumerator Await(System.Threading.Tasks.Task task)
        {
            float end = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Request timed out.");
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.IsCanceled, Is.False);
        }
    }
}
