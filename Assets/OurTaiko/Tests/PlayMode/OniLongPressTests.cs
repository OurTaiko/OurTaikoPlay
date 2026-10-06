using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class OniLongPressTests
    {
        SongSelectScene select;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(.5f);
            select = Object.FindFirstObjectByType<SongSelectScene>();
            SceneSwitcher.Instance.LastDifficulty = -1;
            select.Manager.Confirm();
            yield return new WaitForSecondsRealtime(1f);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            Assert.That(select.CourseFade, Is.EqualTo(1));
            Assert.That(Oni.CanLongPress(), Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
        }

        PointerRelay Oni => select.view.cards[3].click;

        PointerEventData Pointer(int id)
        {
            var rect = select.view.cards[3].board.rectTransform;
            var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var data = new PointerEventData(EventSystem.current)
            {
                pointerId = id, button = PointerEventData.InputButton.Left,
                position = position, eligibleForClick = true
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject), Is.EqualTo(Oni.gameObject));
            return data;
        }

        void Down(PointerEventData data) => ExecuteEvents.Execute(Oni.gameObject, data, ExecuteEvents.pointerDownHandler);
        void Release(PointerEventData data)
        {
            ExecuteEvents.Execute(Oni.gameObject, data, ExecuteEvents.pointerUpHandler);
            // Even a module that still dispatches Click must not start the song after a hold.
            ExecuteEvents.Execute(Oni.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        void TapCourse(int index, int pointerId)
        {
            var card = select.view.cards[index].click.gameObject;
            var data = new PointerEventData(EventSystem.current)
                { pointerId = pointerId, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(card, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(card, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(card, data, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator TappingDifferentCoursesOnlySelectsAndTappingSelectedCourseConfirms()
        {
            foreach (int index in new[] { 0, 1, 2, 3, 0, 3 })
            {
                TapCourse(index, index % 2 == 0 ? -1 : 42);
                Assert.That(select.Manager.Cursor.Selected, Is.EqualTo((Difficulty)index));
                Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
                yield return null;
            }
            TapCourse(3, -1);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Decided));
            Assert.That(SceneSwitcher.Instance.LastDifficulty, Is.EqualTo((int)Difficulty.Oni));
        }

        [UnityTest]
        public IEnumerator UraCardAlsoRequiresSelectionBeforeConfirmation()
        {
            var touch = Pointer(42);
            Down(touch);
            yield return new WaitForSecondsRealtime(1.15f);
            Release(touch);
            yield return new WaitForSecondsRealtime(1.6f);
            TapCourse(2, -1);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Hard));
            TapCourse(3, 42);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Ura));
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            TapCourse(3, 42);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Decided));
            Assert.That(SceneSwitcher.Instance.LastDifficulty, Is.EqualTo((int)Difficulty.Ura));
        }

        [UnityTest]
        public IEnumerator MouseAndTouchToggleOncePerHoldWithoutConfirmingOnRelease()
        {
            var mouse = Pointer(-1);
            Down(mouse);
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(select.Manager.Cursor.IsUra, Is.False, "Less than one second must not toggle.");
            yield return new WaitForSecondsRealtime(.55f);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Ura));
            Assert.That(select.view.uraChange.enabled, Is.True, "Uses the existing flip animation.");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(select.Manager.Cursor.IsUra, Is.True, "Holding longer must not repeat.");
            Release(mouse);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            yield return new WaitForSecondsRealtime(.5f);

            var touch = Pointer(42);
            Down(touch);
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Oni));
            Release(touch);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            yield return new WaitForSecondsRealtime(1.6f);
            var tap = Pointer(-1);
            Down(tap);
            yield return null;
            Release(tap);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.Decided), "A subsequent short tap still confirms.");
        }

        [UnityTest]
        public IEnumerator LeavingCardAndOpeningOptionsCancelPendingHold()
        {
            var touch = Pointer(7);
            Down(touch);
            yield return new WaitForSecondsRealtime(.2f);
            ExecuteEvents.Execute(Oni.gameObject, touch, ExecuteEvents.pointerExitHandler);
            yield return new WaitForSecondsRealtime(1.05f);
            Release(touch);
            Assert.That(select.Manager.Cursor.IsUra, Is.False);
            Assert.That(select.Manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));

            var mouse = Pointer(-1);
            Down(mouse);
            select.Manager.Right(); // Back -> options.
            select.Manager.Confirm();
            yield return new WaitForSecondsRealtime(1.15f);
            Release(mouse);
            Assert.That(select.Manager.Cursor.IsUra, Is.False);
            Assert.That(select.Manager.IsOptionPanelOpen, Is.True);
            Assert.That(select.Manager.OptionMenu.Index, Is.Zero, "The stale release must not confirm an option either.");
        }

        [Test]
        public void SingleSidedChartsCannotToggleAndDrumThresholdIsPreserved()
        {
            var oniOnly = new DifficultyCursor(new[] { Difficulty.Oni }, false, 3);
            Assert.That(oniOnly.TryToggleUra(), Is.False);
            Assert.That(oniOnly.Selected, Is.EqualTo(Difficulty.Oni));
            var uraOnly = new DifficultyCursor(new[] { Difficulty.Ura }, false, 3);
            Assert.That(uraOnly.TryToggleUra(), Is.False);
            Assert.That(uraOnly.Selected, Is.EqualTo(Difficulty.Ura));
            var both = new DifficultyCursor(new[] { Difficulty.Oni, Difficulty.Ura }, false, 3);
            for (int i = 0; i < 9; i++) Assert.That(both.Right(), Is.False);
            Assert.That(both.Right(), Is.True);
            Assert.That(both.Selected, Is.EqualTo(Difficulty.Ura));
        }
    }
}
