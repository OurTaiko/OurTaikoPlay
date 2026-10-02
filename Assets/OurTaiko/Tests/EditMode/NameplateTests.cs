using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NameplateTests
    {
        [Test]
        public void DefaultPlayerIsTheSeededCoinPlate()
        {
            var info = new PlayerInfo();
            Assert.That((info.name, info.title, info.titleBackground, info.dan), Is.EqualTo(("Don-chan", "Donder Debut!", 0, -1)));
            Assert.That(info.HasTitle, Is.False, "The seeded title counts as no title.");
            Assert.That(info.HasDan, Is.False);
            Assert.That(info.IsCoin, Is.True);
        }

        [Test]
        public void TitleOrDanMakesTheBandPlate()
        {
            Assert.That(new PlayerInfo { title = "" }.IsCoin, Is.True);
            Assert.That(new PlayerInfo { title = "Taiko Master" }.IsCoin, Is.False);
            Assert.That(new PlayerInfo { dan = 0 }.IsCoin, Is.False, "初級 is a dan.");
            Assert.That(new PlayerInfo { dan = 24 }.HasDan, Is.True);
            // Out-of-range dan is no dan (the dan = 69 case nameplate.lua guards against).
            Assert.That(new PlayerInfo { dan = 25 }.HasDan, Is.False);
            Assert.That(new PlayerInfo { dan = 69 }.IsCoin, Is.True);
        }

        [Test]
        public void TitleBackgroundOutsideTheFiveFramesFallsBackToZero()
        {
            Assert.That(new PlayerInfo { titleBackground = 4 }.TitleFrame, Is.EqualTo(4));
            Assert.That(new PlayerInfo { titleBackground = 5 }.TitleFrame, Is.Zero);
            Assert.That(new PlayerInfo { titleBackground = -1 }.TitleFrame, Is.Zero);
        }

        [Test]
        public void NameBoxFollowsTheCabinetLabel()
        {
            static (float, float, float) Box(PlayerInfo info)
            {
                var box = NameplateLayout.NameBox(info);
                return (box.X, box.Y, box.FontSize);
            }
            Assert.That(Box(new PlayerInfo()), Is.EqualTo((226f, 53f, 30f)), "1p_coin");
            Assert.That(Box(new PlayerInfo { title = "T" }), Is.EqualTo((226f, 67.5f, 24f)), "1p_shougou");
            Assert.That(Box(new PlayerInfo { dan = 3 }), Is.EqualTo((261f, 67.5f, 24f)), "1p_dani");
            Assert.That(Box(new PlayerInfo { title = "T", dan = 3 }), Is.EqualTo((261f, 67.5f, 24f)), "1p_shougou_dani");
        }

        [Test]
        public void PlayerInfoJsonRoundTripsAndKeepsDefaults()
        {
            var info = new PlayerInfo { name = "どんちゃん", title = "Taiko Master", titleBackground = 3, dan = 24, gold = true, rainbow = true };
            var copy = PlayerInfo.FromJson(info.ToJson());
            Assert.That((copy.name, copy.title, copy.titleBackground, copy.dan, copy.gold, copy.rainbow),
                Is.EqualTo(("どんちゃん", "Taiko Master", 3, 24, true, true)));
            var partial = PlayerInfo.FromJson("{\"name\":\"Katsu\"}");
            Assert.That((partial.name, partial.title, partial.dan), Is.EqualTo(("Katsu", "Donder Debut!", -1)));
            Assert.That(PlayerInfo.FromJson("").name, Is.EqualTo("Don-chan"));
        }

        [Test]
        public void ScoreDigitsAreRightAlignedWithoutPadding()
        {
            Assert.That(ScoreCounterLayout.Text(0), Is.EqualTo("0"));
            Assert.That(ScoreCounterLayout.Text(1006540), Is.EqualTo("1006540"));
            Assert.That(ScoreCounterLayout.Text(-5), Is.EqualTo("0"));
            Assert.That(ScoreCounterLayout.DigitLeft(0, 1), Is.EqualTo(225));
            Assert.That(ScoreCounterLayout.DigitLeft(0, 7), Is.EqualTo(45));
            Assert.That(ScoreCounterLayout.DigitLeft(6, 7), Is.EqualTo(225));
            Assert.That(ScoreCounterLayout.DigitTop, Is.EqualTo(5.5f));
        }
    }
}
