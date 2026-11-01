using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class OffsetSettingsTests
    {
        static SettingsNavigator OnPage(string id)
        {
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Desktop), new GameSettings());
            nav.Move(nav.Pages.ToList().FindIndex(p => p.Id == id));
            nav.Confirm();
            return nav;
        }

        [Test]
        public void AccountTextIsTypedAndSavedWithTheOtherSettings()
        {
            var nav = OnPage("accounts");
            Assert.That(nav.Page.Groups.Select(g => g.Title), Is.EqualTo(new[] { "1P", "2P", "Servers" }));
            Assert.That(nav.Page.Rows.Select(r => r.Label), Is.EqualTo(new[]
            {
                "Username", "Password", "Sign Out", "Username", "Password", "Sign Out", "Refresh Song List", "Refresh Login",
            }));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("Not set"));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Typing));
            // The field owns the keys while typing; only its submit or a step back ends it.
            Assert.That(nav.Move(1), Is.EqualTo(SettingsNavigator.Result.None));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.None));
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(nav.Settings.accounts.p1Username, Is.Empty);
            nav.Confirm();
            Assert.That(nav.SubmitText(" don "), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("don"));
            // The password signs in (the overlay runs its action with the text) and is never kept.
            nav.Move(1); nav.Confirm();
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Password));
            Assert.That(nav.SubmitText("katsu"), Is.EqualTo(SettingsNavigator.Result.Action));
            Assert.That(nav.PendingAction, Is.EqualTo(SettingsCatalog.SignIn + "0"));
            Assert.That(nav.Settings.ToJson(), Does.Not.Contain("katsu"));
            nav.Move(1);
            Assert.That(nav.Row.ActionId, Is.EqualTo(SettingsCatalog.SignOut + "0"));
            nav.Move(3);
            Assert.That(nav.Row.ActionId, Is.EqualTo(SettingsCatalog.SignOut + "1"));

            // The buttons open nothing: the overlay runs them and writes their status.
            nav.Move(1);
            Assert.That(nav.Row.ActionId, Is.EqualTo(SettingsCatalog.RefreshSongList));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Action));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            nav.Row.Status = "Done";
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("Done"));

            string json = nav.Settings.ToJson();
            Assert.That(json, Does.Contain("p2Username"));
            var restored = GameSettings.FromJson(json);
            Assert.That(restored.accounts.p1Username, Is.EqualTo("don"));
            Assert.That(restored.accounts.p1Password, Is.Empty);
            // What an older version saved is still read, for the one sign-in that erases it.
            Assert.That(GameSettings.FromJson("{\"accounts\":{\"p1Password\":\"old\"}}").accounts.p1Password, Is.EqualTo("old"));
            Assert.That(GameSettings.FromJson("{}").accounts.p1Username, Is.Empty);
        }

        [Test]
        public void NumbersStageSignedStepsAndOnlyConfirmPersists()
        {
            var nav = OnPage("play");
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Number));
            Assert.That(nav.Row.Default, Is.Zero);
            Assert.That(nav.Row.Step, Is.EqualTo(1));
            nav.Confirm();
            nav.Move(-1); nav.Move(-1);
            Assert.That(nav.AdjustValue, Is.EqualTo(-2));
            Assert.That(nav.Row.Format(nav.AdjustValue), Is.EqualTo("-2 ms"));
            Assert.That(nav.Row.Format(3), Is.EqualTo("+3 ms"));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.Zero);
            nav.Back(); nav.Confirm();
            Assert.That(nav.AdjustValue, Is.Zero);
            nav.Move(1); nav.Confirm();
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(1));
            nav.Move(1);
            nav.Side(-1);
            var restored = GameSettings.FromJson(nav.Settings.ToJson());
            Assert.That(restored.play.audioOffsetMs, Is.EqualTo(1));
            Assert.That(restored.play.judgeOffsetMs, Is.EqualTo(-1));
            Assert.That(GameSettings.FromJson("{\"play\":{\"singlePlayerDrumPad\":false}}").play.audioOffsetMs, Is.Zero);
            Assert.That(GameSettings.FromJson("{}").play.judgeOffsetMs, Is.Zero);
        }

        [Test]
        public void InputDelayDefaultsToZeroAndStaysInRange()
        {
            Assert.That(new GameSettings().play.inputDelayMs, Is.Zero);
            Assert.That(GameSettings.FromJson("{\"play\":{\"judgeOffsetMs\":3}}").play.inputDelayMs, Is.Zero, "Older files get the default.");
            var nav = OnPage("play");
            nav.Move(2); nav.Confirm();
            Assert.That(nav.Row.Label, Is.EqualTo("Input Delay"));
            Assert.That(nav.AdjustValue, Is.Zero);
            for (int i = 0; i < 5; i++) nav.Move(1);
            Assert.That(nav.AdjustValue, Is.EqualTo(5));
            for (int i = 0; i < 8; i++) nav.Move(-1);
            Assert.That(nav.AdjustValue, Is.Zero, "A delay cannot be negative.");
            nav.Confirm();
            Assert.That(GameSettings.FromJson(nav.Settings.ToJson()).play.inputDelayMs, Is.Zero);
            Assert.That(nav.Row.Move(PlaySettings.InputDelayMaxMs, 1), Is.EqualTo(PlaySettings.InputDelayMaxMs));
        }

        [Test]
        public void NumericMetadataWorksForOtherDefaultsAndStepsWithoutWrapping()
        {
            var row = SettingRow.Number("test", "Test", "", 10, 5, s => s.play.audioOffsetMs,
                (s, value) => s.play.audioOffsetMs = value, v => v + " ms");
            Assert.That(row.Move(row.Default, -3), Is.EqualTo(-5));
            Assert.That(row.Move(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
            Assert.That(row.Move(int.MinValue, -1), Is.EqualTo(int.MinValue));
        }

        [TestCase(-0.2)] [TestCase(0.2)]
        public void BMovesJudgmentAndMissWindowButAutoplayStaysOnTheChart(double offset)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n0010,\n#END");
            var play = new PlaySession(chart, offset);
            Assert.That(play.Hit(false, 1 + offset), Is.EqualTo(Judgment.Good));
            play = new PlaySession(chart, offset);
            play.Advance(1 + offset + PlaySession.BadWindow - .001, false);
            Assert.That(play.Bad, Is.Zero);
            play.Advance(1 + offset + PlaySession.BadWindow + .001, false);
            Assert.That(play.Bad, Is.EqualTo(1));
            play = new PlaySession(chart, offset);
            play.Advance(.999, true);
            Assert.That(play.Good, Is.Zero);
            play.Advance(1, true);
            Assert.That(play.Good, Is.EqualTo(1));
        }

        [TestCase(-0.2)] [TestCase(0.2)]
        public void PracticePreservesBAndLongNotesUseTheShiftedWindow(double offset)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:10\n#START\n0078,\n0010,\n#END");
            var play = PlaySession.PracticeAt(chart, 1, new PlaySession(chart, offset));
            Assert.That(play.JudgeOffset, Is.EqualTo(offset));
            Assert.That(play.Hit(false, 1 + offset - .001), Is.EqualTo(Judgment.None));
            Assert.That(play.Hit(false, 1 + offset), Is.EqualTo(Judgment.Roll));
            Assert.That(play.Hit(false, 1.5 + offset), Is.EqualTo(Judgment.Roll));
            Assert.That(play.Hit(false, 1.5 + offset + .001), Is.EqualTo(Judgment.None));
            Assert.That(play.Hit(false, 3 + offset), Is.EqualTo(Judgment.Good));
        }
    }
}
