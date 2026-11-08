using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace OurTaiko.Tests
{
    public sealed class KeyboardBindingsTests
    {
        [TearDown] public void Restore() => InputManager.ResetBindings();

        [Test]
        public void OldSettingsKeepDefaultsAndEmptyBindingsSurviveReload()
        {
            var old = GameSettings.FromJson("{\"general\":{\"language\":\"ja\"}}");
            Assert.That(old.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            old.general.keyboard.Set(InputKey.LeftDon);
            old.general.keyboard.Set(InputKey.RightDon, Key.A, Key.S, Key.S);
            var loaded = old.Clone();
            Assert.That(loaded.general.keyboard.Get(InputKey.LeftDon), Is.Empty);
            Assert.That(loaded.general.keyboard.Get(InputKey.RightDon), Is.EqualTo(new[] { Key.A, Key.S }));
            Assert.That(loaded.general.language, Is.EqualTo("ja"));
            loaded.general.keyboard.Set(InputKey.RightDon);
            Assert.That(old.general.keyboard.Get(InputKey.RightDon).Count, Is.EqualTo(2));
        }

        [Test]
        public void MalformedBindingsAreNormalizedAndExplicitKeysWinOverMissingDefaults()
        {
            var settings = GameSettings.FromJson("{\"general\":{\"keyboard\":{\"bindings\":[{\"action\":0,\"keys\":[19,19,0,9999]},{\"action\":99,\"keys\":[1]}]}}}");
            var bindings = settings.general.keyboard;
            var all = bindings.bindings.SelectMany(b => b.keys).ToArray();
            Assert.That(bindings.bindings.Length, Is.EqualTo(Enum.GetValues(typeof(InputKey)).Length));
            Assert.That(all, Is.Unique);
            Assert.That(all, Has.None.EqualTo(Key.None));
            Assert.That(all.All(k => Enum.IsDefined(typeof(Key), k)), Is.True);
            var partial = new KeyboardBindings { bindings = new[] { new KeyboardBindings.Binding(InputKey.LeftDon, Key.J) } };
            partial.Normalize();
            Assert.That(partial.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.J }));
            Assert.That(partial.Get(InputKey.RightDon), Is.Empty);
        }

        [Test]
        public void SpaceIsNotADefaultKey()
        {
            Assert.That(new GameSettings().general.keyboard.bindings.SelectMany(b => b.keys), Has.None.EqualTo(Key.Space));
            Assert.That(KeyboardBindings.DefaultKeys(InputKey.Pause), Is.Empty);
        }

        [Test]
        public void AssignedKeysMoveOwnersAndRuntimeBindingsMatchSavedSettings()
        {
            var bindings = new KeyboardBindings();
            bindings.Set(InputKey.LeftDon, Key.J, Key.A);
            Assert.That(bindings.Get(InputKey.RightDon), Is.Empty);
            bindings.Apply();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.J, Key.A }));
            Assert.That(InputManager.GetBinding(InputKey.RightDon), Is.Empty);
            InputManager.SetBinding(InputKey.RightDon, Key.A, Key.A, Key.None, (Key)999);
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.J }));
            Assert.That(InputManager.GetBinding(InputKey.RightDon), Is.EqualTo(new[] { Key.A }));
            InputManager.ResetBindings();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
        }

        [Test]
        public void MenuAddsRemovesClearsResetsAndCancelsWithoutReservingKeys()
        {
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Desktop), new GameSettings());
            nav.Move(1); nav.Confirm();
            Assert.That(nav.Page.Id, Is.EqualTo("controls"));
            Assert.That(nav.Row.Key, Is.EqualTo(InputKey.LeftDon));
            nav.Confirm(); nav.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Capture));
            Assert.That(nav.Move(1), Is.EqualTo(SettingsNavigator.Result.None));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.None));
            Assert.That(nav.CaptureKey(Key.Escape), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.Back), Is.Empty);
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            nav.Confirm();
            Assert.That(nav.SheetOptions, Is.EqualTo(new[] { "Add a Key", "Remove F", "Remove Escape", "Reset to Default", "Clear All" }));
            nav.TapSheet(1);
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.Escape }));
            nav.Confirm(); nav.TapSheet(3);
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.Empty);
            nav.Confirm(); nav.TapSheet(1);
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            nav.Confirm(); nav.TapSheet(0); nav.Back();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Sheet));
            Assert.That(nav.CaptureKey(Key.A), Is.EqualTo(SettingsNavigator.Result.None));
            nav.Back();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
        }
    }
}
