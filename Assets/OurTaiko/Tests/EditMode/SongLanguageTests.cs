using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SongLanguageTests
    {
        const string Chart = "TITLE:Base title\nSUBTITLE:--Base subtitle\nTITLEJA:日本語の曲名\nSUBTITLEJA:--日本語の副題\nTITLEZH:中文歌名\nTITLEZH_TW:繁體歌名\nSUBTITLEKO:--한국어\nBPM:120\nCOURSE:Oni\nLEVEL:3\n#START\n1000,\n#END";

        [TestCase("zh", "中文歌名", "Base subtitle")]
        [TestCase("zh_tw", "繁體歌名", "Base subtitle")]
        [TestCase("ko", "Base title", "한국어")]
        [TestCase("ja", "日本語の曲名", "日本語の副題")]
        [TestCase("en", "Base title", "Base subtitle")]
        public void SelectedLanguageFallsBackToTheOriginalPerField(string language, string title, string subtitle)
        {
            var info = SongInfo.Read(Chart, language);
            Assert.That(info.Title, Is.EqualTo(title));
            Assert.That(info.Subtitle, Is.EqualTo(subtitle));
            Assert.That(info.Courses[0].Level, Is.EqualTo(3));
            Assert.That(TjaParser.Parse(Chart).Title, Is.EqualTo("Base title"), "Display language must not rewrite the playable chart.");
        }

        [Test]
        public void MissingAndEmptyTranslationsUseTheOriginalTitle()
        {
            Assert.That(SongInfo.Read(Chart + "\nTITLEZH:  ", "zh").Title, Is.EqualTo("Base title"), "No Japanese fallback.");
            Assert.That(SongInfo.Read("TITLE:Base\nTITLEJP:日本語", "ko").Title, Is.EqualTo("Base"));
            Assert.That(SongInfo.Read("TITLE:Base\nTITLEJP:日本語", "ja").Title, Is.EqualTo("日本語"));
            Assert.That(SongInfo.Read("TITLE:Base", "ko").Title, Is.EqualTo("Base"));
            Assert.That(SongInfo.Read("TITLEJA:日本語", "en").Title, Is.EqualTo("Untitled"), "Without TITLE there is no original to show.");
            Assert.That(SongInfo.Read("TITLECN:简体\nTITLETW:繁體", "zh_tw").Title, Is.EqualTo("繁體"));
        }

        [Test]
        public void OnlineMetadataUsesTheChosenTranslationThenTheOriginal()
        {
            var chart = new Online.FanmadeChart { Title = "原題", Subtitle = "原副題" };
            chart.Titles["en"] = "Base";
            chart.Titles["ja"] = "日本語";
            chart.Titles["zh"] = "中文";
            chart.Subtitles["ja"] = "日本語副題";
            var selected = chart.ToSongInfo("zh");
            Assert.That(selected.Title, Is.EqualTo("中文"));
            Assert.That(selected.Subtitle, Is.EqualTo("原副題"), "No Japanese fallback for online songs.");
            Assert.That(chart.ToSongInfo("zh_tw").Title, Is.EqualTo("原題"));
            Assert.That(chart.ToSongInfo("ja").Subtitle, Is.EqualTo("日本語副題"));
            Assert.That(SongInfo.Read(chart.TitleHeaders(), "zh_tw").Title, Is.EqualTo("原題"), "The playable copy keeps the original as TITLE.");
            Assert.That(SongInfo.Read(chart.TitleHeaders(), "en").Title, Is.EqualTo("Base"));
        }

        [Test]
        public void GeneralLanguageIsSavedAndOldSettingsKeepTheirDefaults()
        {
            var settings = GameSettings.FromJson("{\"general\":null,\"audio\":{\"deviceBufferMs\":73}}");
            Assert.That(settings.general.Language, Is.EqualTo("en"));
            var menu = new SettingsMenu(SettingsMenu.Catalog(), settings);
            Assert.That(menu.CurrentType.Label, Is.EqualTo("General"));
            menu.Don(); menu.Don();
            menu.TapChoice(3);
            var saved = GameSettings.FromJson(menu.Settings.ToJson());
            Assert.That(saved.general.language, Is.EqualTo("zh_tw"));
            Assert.That(saved.audio.deviceBufferMs, Is.EqualTo(73));
            Assert.That(saved.play.singlePlayerDrumPad, Is.True);
            Assert.That(GameSettings.FromJson("{}").general.Language, Is.EqualTo("en"));
            saved.general.language = "unknown";
            Assert.That(saved.general.Language, Is.EqualTo("en"));
        }
    }
}
