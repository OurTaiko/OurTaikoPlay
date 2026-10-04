using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SettingsMenuTests
    {
        static SettingsMenu Menu(bool drumPad = true)
        {
            var menu = new SettingsMenu(SettingsMenu.Catalog(), new GameSettings { play = new PlaySettings { singlePlayerDrumPad = drumPad } });
            menu.Ka(1); // Play follows General.
            return menu;
        }

        [Test]
        public void DrumKeysWalkTypesItemsAndChoicesWithReturnEntries()
        {
            var menu = Menu();
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types), "The focus starts on the types.");
            Assert.That(menu.Types[0].Label, Is.EqualTo("General"));
            Assert.That(menu.Types[1].Label, Is.EqualTo("Play"));
            Assert.That(menu.TypeCount, Is.EqualTo(5), "General, Play, Display, Sound and Return.");

            // ka wraps through Play, Display and Return; the item focus does not move with it.
            Assert.That(menu.Ka(-2), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.IsTypeReturn, Is.True);
            menu.Ka(2);
            Assert.That(menu.TypeIndex, Is.EqualTo(1));
            menu.Ka(1);
            Assert.That(menu.CurrentType.Label, Is.EqualTo("Display"));
            menu.Ka(-1);

            // don on Play focuses its items: the drum pad setting, A/B offsets, then Return.
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Entered));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.ItemCount, Is.EqualTo(4));
            Assert.That(menu.CurrentItem.Label, Is.EqualTo("Enable Drumpad for Single Player Mode"));

            // don on the item opens its choices on the current value; ka moves, don applies and
            // hands the focus back to the items.
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Entered));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Choice));
            Assert.That(menu.CurrentItem.Choices, Is.EqualTo(new[] { "Enabled", "Disabled" }));
            Assert.That(menu.ChoiceIndex, Is.Zero, "Enabled is the default.");
            menu.Ka(1);
            Assert.That(menu.Settings.play.singlePlayerDrumPad, Is.True, "Moving does not apply.");
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Changed));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.Settings.play.singlePlayerDrumPad, Is.False);

            // The items' Return goes back to the types; the types' Return leaves.
            menu.Ka(3);
            Assert.That(menu.IsItemReturn, Is.True);
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Returned));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
            Assert.That(menu.TypeIndex, Is.EqualTo(1), "The type stays where it was.");
            menu.Ka(-2);
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Exit));
        }

        [Test]
        public void ChoicesOpenOnTheCurrentValueAndBackChangesNothing()
        {
            var menu = Menu(drumPad: false);
            menu.Don(); menu.Don();
            Assert.That(menu.ChoiceIndex, Is.EqualTo(1), "Disabled is current.");
            menu.Ka(-1);
            Assert.That(menu.Back(), Is.EqualTo(SettingsMenu.Result.Returned));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.Settings.play.singlePlayerDrumPad, Is.False);
            Assert.That(menu.Back(), Is.EqualTo(SettingsMenu.Result.Returned));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
            Assert.That(menu.Back(), Is.EqualTo(SettingsMenu.Result.Exit));
        }

        [Test]
        public void TapsAndSwipesFollowSongSelectRules()
        {
            var menu = Menu();
            // Tapping another type selects it; tapping the focused one confirms it.
            Assert.That(menu.TapType(menu.Types.Count), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.IsTypeReturn, Is.True);
            Assert.That(menu.TapType(1), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.TapType(1), Is.EqualTo(SettingsMenu.Result.Entered));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));

            // An item tap from the types moves the focus to the items; a second tap opens the choices.
            menu.TapType(1);
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
            Assert.That(menu.TapItem(0), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.TapItem(0), Is.EqualTo(SettingsMenu.Result.Entered));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Choice));

            // A tap on a choice applies it at once.
            Assert.That(menu.TapChoice(1), Is.EqualTo(SettingsMenu.Result.Changed));
            Assert.That(menu.Settings.play.singlePlayerDrumPad, Is.False);
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));

            // Swipes move the focus into the swiped list, then through it (wrapping).
            Assert.That(menu.SwipeTypes(-2), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
            Assert.That(menu.IsTypeReturn, Is.True);
            Assert.That(menu.SwipeItems(1), Is.EqualTo(SettingsMenu.Result.None), "Return has no items.");
            menu.SwipeTypes(2);
            Assert.That(menu.SwipeItems(3), Is.EqualTo(SettingsMenu.Result.Moved));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.IsItemReturn, Is.True);
        }

        [Test]
        public void SettingsJsonKeepsDefaultsForMissingFields()
        {
            Assert.That(new GameSettings().play.singlePlayerDrumPad, Is.True);
            Assert.That(GameSettings.FromJson("{}").play.singlePlayerDrumPad, Is.True);
            Assert.That(GameSettings.FromJson("").play.singlePlayerDrumPad, Is.True);
            var off = new GameSettings { play = new PlaySettings { singlePlayerDrumPad = false } };
            Assert.That(GameSettings.FromJson(off.ToJson()).play.singlePlayerDrumPad, Is.False);
            Assert.That(GameSettings.FromJson("{\"play\":{}}").display.targetFrameRate, Is.EqualTo(120));
            Assert.That(GameSettings.FromJson("{\"display\":{\"targetFrameRate\":60}}").display.vSync, Is.False);
        }

        [Test]
        public void TargetFrameRateOffers120By60AndUnlimited()
        {
            var menu = Menu();
            menu.Ka(1); menu.Don();
            Assert.That(menu.CurrentItem.Label, Is.EqualTo("Target Frame Rate"));
            menu.Don();
            Assert.That(menu.CurrentItem.Choices, Is.EqualTo(new[] { "120 FPS", "60 FPS", "Unlimited" }));
            Assert.That(menu.ChoiceIndex, Is.Zero, "120 FPS is the default.");
            menu.Ka(1); menu.Don();
            Assert.That(menu.Settings.display.targetFrameRate, Is.EqualTo(60));
            menu.Don(); menu.Ka(1); menu.Don();
            Assert.That(menu.Settings.display.targetFrameRate, Is.EqualTo(DisplaySettings.Unlimited));
            Assert.That(menu.Settings.display.TargetFrameRate, Is.EqualTo(-1));
            Assert.That(GameSettings.FromJson(menu.Settings.ToJson()).display.targetFrameRate, Is.EqualTo(-1));

            // An unknown value shows and applies as the default.
            var odd = new GameSettings { display = new DisplaySettings { targetFrameRate = 75 } };
            Assert.That(odd.display.TargetFrameRate, Is.EqualTo(120));
            menu = new SettingsMenu(SettingsMenu.Catalog(), odd);
            menu.Ka(2); menu.Don(); menu.Don();
            Assert.That(menu.ChoiceIndex, Is.Zero);
        }

        [Test]
        public void VSyncIsADisplayToggleOffByDefault()
        {
            var menu = Menu();
            menu.Ka(1); menu.Don(); menu.Ka(1);
            Assert.That(menu.CurrentItem.Label, Is.EqualTo("VSync"));
            Assert.That(menu.ItemCount, Is.EqualTo(3), "Target Frame Rate, VSync and Return.");
            menu.Don();
            Assert.That(menu.CurrentItem.Choices, Is.EqualTo(new[] { "Enabled", "Disabled" }));
            Assert.That(menu.ChoiceIndex, Is.EqualTo(1), "Disabled is the default.");
            menu.Ka(-1); menu.Don();
            Assert.That(menu.Settings.display.vSync, Is.True);
            Assert.That(GameSettings.FromJson(menu.Settings.ToJson()).display.vSync, Is.True);
        }

        [Test]
        public void EntryModeListClampsAtBothEnds()
        {
            var flow = new EntryFlow(0, 2);
            Assert.That(flow.MoveMode(1, 10), Is.False, "No moves before the list is up.");
            flow.Join(EntryFlow.SideInputLockMs);
            double ready = EntryFlow.SideInputLockMs + EntryFlow.CloudGateMs + 1;
            Assert.That(flow.MoveMode(-1, ready), Is.False);
            Assert.That(flow.MoveMode(1, ready), Is.True);
            Assert.That(flow.SelectedMode, Is.EqualTo(1));
            Assert.That(flow.MoveMode(1, ready), Is.False);
            flow.Select(ready);
            Assert.That(flow.MoveMode(-1, ready), Is.False, "No moves after the decide.");
        }
    }
}
