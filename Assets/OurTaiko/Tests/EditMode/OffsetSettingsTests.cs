using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class OffsetSettingsTests
    {
        [Test]
        public void AccountTextIsTypedAndSavedWithTheOtherSettings()
        {
            var menu = new SettingsMenu(SettingsMenu.Catalog(), new GameSettings());
            menu.Ka(4);
            Assert.That(menu.CurrentType.Label, Is.EqualTo("OurTaiko Accounts"));
            Assert.That(menu.CurrentType.Items.Select(i => i.Label), Is.EqualTo(new[] { "1P Username", "1P Password", "Refresh Song List", "Refresh Login Status" }), "2P is not listed yet.");
            menu.Don();
            Assert.That(menu.CurrentItem.Format(menu.Settings), Is.EqualTo("Not set"));
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Entered));
            Assert.That(menu.EditingText, Is.True);
            // The field owns the keys while typing; only its submit or a step back ends it.
            Assert.That(menu.Ka(1), Is.EqualTo(SettingsMenu.Result.None));
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.None));
            Assert.That(menu.Back(), Is.EqualTo(SettingsMenu.Result.Returned));
            Assert.That(menu.Settings.accounts.p1Username, Is.Empty);
            menu.Don();
            Assert.That(menu.SubmitText(" don "), Is.EqualTo(SettingsMenu.Result.Changed));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(menu.CurrentItem.Format(menu.Settings), Is.EqualTo("don"));
            menu.Ka(1); menu.Don();
            Assert.That(menu.CurrentItem.IsSecret, Is.True);
            menu.SubmitText("katsu");
            Assert.That(menu.CurrentItem.Format(menu.Settings), Is.EqualTo("*****"), "A password is never shown.");

            // The buttons open nothing: the scene runs them and writes their status.
            menu.Ka(1);
            Assert.That(menu.CurrentItem.ActionId, Is.EqualTo(SettingsMenu.RefreshSongListAction));
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.Action));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
            menu.CurrentItem.Status = "Done";
            Assert.That(menu.CurrentItem.Format(menu.Settings), Is.EqualTo("Done"));

            string json = menu.Settings.ToJson();
            Assert.That(json, Does.Contain("p2Username").And.Contain("p2Password"), "2P is kept in the file.");
            var restored = GameSettings.FromJson(json);
            Assert.That(restored.accounts.p1Username, Is.EqualTo("don"));
            Assert.That(restored.accounts.p1Password, Is.EqualTo("katsu"));
            Assert.That(GameSettings.FromJson("{}").accounts.p1Username, Is.Empty);
        }

        [Test]
        public void NumbersStageSignedStepsAndOnlyConfirmPersists()
        {
            var menu = new SettingsMenu(SettingsMenu.Catalog(), new GameSettings());
            menu.Ka(1); menu.Don(); menu.Ka(1); menu.Don();
            Assert.That(menu.CurrentItem.IsNumber, Is.True);
            Assert.That(menu.CurrentItem.DefaultValue, Is.Zero);
            Assert.That(menu.CurrentItem.Step, Is.EqualTo(1));
            menu.Ka(-1); menu.Ka(-1);
            Assert.That(menu.ChoiceIndex, Is.EqualTo(-2));
            Assert.That(menu.CurrentItem.Format(menu.ChoiceIndex), Is.EqualTo("-2 ms"));
            Assert.That(menu.Settings.play.audioOffsetMs, Is.Zero);
            menu.Back(); menu.Don();
            Assert.That(menu.ChoiceIndex, Is.Zero);
            menu.Ka(1); menu.Don();
            Assert.That(menu.Settings.play.audioOffsetMs, Is.EqualTo(1));
            menu.Ka(1); menu.Don(); menu.Ka(-1); menu.TapChoice(0);
            var restored = GameSettings.FromJson(menu.Settings.ToJson());
            Assert.That(restored.play.audioOffsetMs, Is.EqualTo(1));
            Assert.That(restored.play.judgeOffsetMs, Is.EqualTo(-1));
            Assert.That(GameSettings.FromJson("{\"play\":{\"singlePlayerDrumPad\":false}}").play.audioOffsetMs, Is.Zero);
            Assert.That(GameSettings.FromJson("{}").play.judgeOffsetMs, Is.Zero);
        }

        [Test]
        public void NumericMetadataWorksForOtherDefaultsAndStepsWithoutWrapping()
        {
            var item = SettingItem.Number("Test", "", 10, 5, "ms", s => s.play.audioOffsetMs,
                (s, value) => s.play.audioOffsetMs = value);
            Assert.That(item.Move(item.DefaultValue, -3), Is.EqualTo(-5));
            Assert.That(item.Move(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
            Assert.That(item.Move(int.MinValue, -1), Is.EqualTo(int.MinValue));
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
