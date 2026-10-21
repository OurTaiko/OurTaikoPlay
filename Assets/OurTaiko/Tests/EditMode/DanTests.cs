using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class DanTests
    {
        const string Hash1 = "1111111111111111111111111111111111111111111111111111111111111111";
        const string Hash2 = "2222222222222222222222222222222222222222222222222222222222222222";
        readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
        }

        static string Course(string songs, string exams = "[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}]", string extra = "")
            => "{\"format\":1,\"id\":\"local:test\",\"title\":{\"ja\":\"十段\",\"en\":\"10th Dan\"},\"color\":3,\"songs\":" + songs + ",\"exams\":" + exams + extra + "}";

        static string Songs(int count) => "[" + string.Join(",", Enumerable.Range(0, count).Select(i => "{\"chart\":{\"sha256\":\"" + Hash1 + "\",\"course\":\"Oni\"}}")) + "]";

        static DanCourse Parse(string songs, string exams) => DanCourse.Parse(Course(songs, exams));

        SongDefinition Song(string title, string sha256, string courses = "COURSE:Oni\nLEVEL:10\n#START\n1111,\n#END")
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            var chart = new TextAsset("TITLE:" + title + "\nBPM:120\n" + courses);
            song.chart = chart; song.localChartSha256 = sha256; song.name = title;
            created.Add(song); created.Add(chart);
            return song;
        }

        [Test]
        public void ParsesTitlesSongsAndBothExamShapes()
        {
            var course = Parse(Songs(3), "[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]},"
                + "{\"type\":\"ok\",\"range\":\"less\",\"value\":[[30,15],null,[20]]}]");
            Assert.That(course.Id, Is.EqualTo("local:test"));
            Assert.That(course.Title("ja"), Is.EqualTo("十段"));
            Assert.That(course.Title("zh"), Is.EqualTo("10th Dan"), "Falls back to English.");
            Assert.That(course.Color, Is.EqualTo(3));
            Assert.That(course.IsLocal, Is.True);
            Assert.That(course.Songs.Select(s => s.Difficulty), Is.All.EqualTo(Difficulty.Oni));

            var gauge = course.Exams[0];
            Assert.That(gauge.PerSong, Is.False);
            Assert.That(gauge.Course.Red, Is.EqualTo(100));
            Assert.That(gauge.Course.Gold, Is.EqualTo(100), "A lone value is both borders.");

            var ok = course.Exams[1];
            Assert.That(ok.Type, Is.EqualTo(DanExamType.Ok));
            Assert.That(ok.Range, Is.EqualTo(DanExamRange.Less));
            Assert.That(ok.PerSong, Is.True);
            Assert.That(ok.BorderFor(0)?.Gold, Is.EqualTo(15));
            Assert.That(ok.BorderFor(1), Is.Null, "null: the second song has no border.");
            Assert.That(ok.BorderFor(2)?.Red, Is.EqualTo(20));
            Assert.That(ok.BorderFor(2)?.Gold, Is.EqualTo(20));
        }

        [Test]
        public void GoldOnTheWrongSideIsResolvedToThePerfectValue()
        {
            var course = Parse(Songs(1), "[{\"type\":\"bad\",\"range\":\"less\",\"value\":[5,9]},"
                + "{\"type\":\"good\",\"range\":\"more\",\"value\":[500,400]},{\"type\":\"score\",\"range\":\"more\",\"value\":[9000,1]}]");
            Assert.That(course.Exams.Select(e => e.Course.Gold), Is.All.EqualTo(DanExam.GoldFull));
            Assert.That(course.Exams[0].Resolve(course.Exams[0].Course, 800).Gold, Is.EqualTo(1), "less: gold is none at all.");
            Assert.That(course.Exams[1].Resolve(course.Exams[1].Course, 800).Gold, Is.EqualTo(800), "良: every note.");
            Assert.That(course.Exams[2].Resolve(course.Exams[2].Course, 800).Gold, Is.EqualTo(9000), "score: the red itself.");
        }

        [TestCase("{\"format\":2,\"id\":\"x\",\"title\":\"x\",\"songs\":[]}", "format")]
        [TestCase("{\"format\":1,\"title\":\"x\",\"songs\":[]}", "id")]
        [TestCase("{\"format\":1,\"id\":\"x\",\"songs\":[]}", "title")]
        [TestCase("{\"format\":1,\"id\":\"x\",\"title\":\"x\",\"songs\":[]}", "songs")]
        [TestCase("not json", "JSON")]
        public void RejectsMalformedCourses(string json, string reason)
        {
            var error = Assert.Throws<FormatException>(() => DanCourse.Parse(json));
            Assert.That(error.Message, Does.Contain(reason));
        }

        [Test]
        public void RejectsMoreThanThreeSongsAndBadExams()
        {
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(4), "[]")).Message, Does.Contain("at most 3"));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(1), "[{\"type\":\"accuracy\",\"range\":\"more\",\"value\":[90]}]")).Message, Does.Contain("type"));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(1), "[{\"type\":\"bad\",\"range\":\"under\",\"value\":[9]}]")).Message, Does.Contain("range"));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(2), "[{\"type\":\"bad\",\"range\":\"less\",\"value\":[[9],[9],[9]]}]")).Message, Does.Contain("per-song"));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(1), "[{\"type\":\"bad\",\"range\":\"less\",\"value\":[1,2,3]}]")).Message, Does.Contain("borders"));
            Assert.That(Assert.Throws<FormatException>(() => DanCourse.Parse(Course("[{\"chart\":{\"course\":\"Oni\"}}]"))).Message, Does.Contain("sha256"));
            Assert.That(Assert.Throws<FormatException>(() => DanCourse.Parse(Course("[{\"chart\":{\"sha256\":\"" + Hash1 + "\",\"course\":\"Tower\"}}]"))).Message, Does.Contain("course"));
        }

        [Test]
        public void LocalRewardsAreIgnoredWithAWarningAndServerSongsNeedAnId()
        {
            var local = DanCourse.Parse(Course(Songs(1), extra: ",\"rewards\":{\"dan\":20}"));
            Assert.That(local.Warnings, Has.Some.Contains("rewards"));
            Assert.That(Assert.Throws<FormatException>(() => DanCourse.Parse(Course(Songs(1)), "https://example.test")).Message, Does.Contain("id"));
            var online = DanCourse.Parse(Course("[{\"chart\":{\"id\":\"42\",\"course\":\"Hard\"}}]", extra: ",\"rewards\":{\"dan\":20}"), "https://example.test");
            Assert.That(online.IsLocal, Is.False);
            Assert.That(online.Warnings, Is.Empty);
            Assert.That(online.Songs[0].Id, Is.EqualTo("42"));
            Assert.That(online.Songs[0].Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(DanCourse.Parse(Course(Songs(1), "[]")).Warnings, Has.Some.Contains("No exams"));
        }

        [Test]
        public void TiersCountAGoldEqualToItsRedAsGold()
        {
            var more = new DanExam { Type = DanExamType.Gauge, Range = DanExamRange.More };
            Assert.That(more.Tier(new DanBorder(100, 100), 99), Is.EqualTo(0));
            Assert.That(more.Tier(new DanBorder(100, 100), 100), Is.EqualTo(2), "YataiDON capped this at red.");
            Assert.That(more.Tier(new DanBorder(80, 100), 90), Is.EqualTo(1));
            var less = new DanExam { Type = DanExamType.Bad, Range = DanExamRange.Less };
            Assert.That(less.Tier(new DanBorder(10, 5), 10), Is.EqualTo(0));
            Assert.That(less.Tier(new DanBorder(10, 5), 5), Is.EqualTo(1));
            Assert.That(less.Tier(new DanBorder(10, 5), 4), Is.EqualTo(2));
        }

        [Test]
        public void BarStatesFollowYataiDon()
        {
            var more = new DanExam { Type = DanExamType.Gauge, Range = DanExamRange.More };
            var b = new DanBorder(100, 100);
            Assert.That(more.BarState(b, 0), Is.EqualTo("empty"));
            Assert.That(more.BarState(b, 30), Is.EqualTo("up_50"));
            Assert.That(more.BarState(b, 80), Is.EqualTo("up_80"));
            Assert.That(more.BarState(b, 100, live: true), Is.EqualTo("max"));
            var less = new DanExam { Type = DanExamType.Bad, Range = DanExamRange.Less };
            var l = new DanBorder(10, 5);
            Assert.That(less.BarState(l, 0), Is.EqualTo("max"), "After play a less exam under its gold is max.");
            Assert.That(less.BarState(l, 0, live: true), Is.EqualTo("down_100"), "During play it never shows max.");
            Assert.That(less.BarState(l, 0, live: true, nearEnd: true), Is.EqualTo("max_soon"));
            Assert.That(less.BarState(l, 0, live: true, nearEnd: true, justBeforeEnd: true), Is.EqualTo("max_soon2"));
            Assert.That(less.BarState(l, 6, live: true), Is.EqualTo("up_80"));
            Assert.That(less.BarState(l, 8, live: true), Is.EqualTo("down_80"));
            Assert.That(less.BarState(l, 10, live: true), Is.EqualTo("empty"));
        }

        [Test]
        public void DanGaugeUsesTheHarmonicMeanAndHasNoNormaZone()
        {
            var single = SoulGauge.Dan(new[] { ("Oni", 10) }, 500);
            var chart = new SoulGauge(500, "Oni", 10);
            Assert.That(single.GoodPoints, Is.EqualTo(chart.GoodPoints).Within(1e-9));
            Assert.That(single.ClearPoints, Is.EqualTo(SoulGauge.MaximumPoints));

            var mixed = SoulGauge.Dan(new[] { ("Oni", 10), ("Hard", 3), ("Oni", 0) }, 1200);
            double hard = 72.5, oni = 76.75;
            double soul = 3 / (1 / oni + 1 / hard + 1 / oni);
            Assert.That(mixed.GoodPoints, Is.EqualTo(1000000.0 / (1200 * soul)).Within(1e-9), "LEVEL 0 counts as Oni 10.");
            Assert.That(mixed.OkPoints, Is.EqualTo(mixed.GoodPoints * (0.5 + 0.75 + 0.5) / 3).Within(1e-9));
            Assert.That(mixed.BadPoints, Is.EqualTo(mixed.GoodPoints * (-2.0 - 1.0 - 2.0) / 3).Within(1e-9));
            mixed.ApplyJudgment(Judgment.Good);
            Assert.That(mixed.IsClear, Is.False, "Only a full gauge passes.");
        }

        static DanProgress Progress(string exams, int songs = 3, int notesEach = 100)
            => new DanProgress(Parse(Songs(songs), exams), Enumerable.Repeat(notesEach, songs).ToList());

        [Test]
        public void LessExamsFailTheMomentTheirAllowanceIsUsedUp()
        {
            var progress = Progress("[{\"type\":\"bad\",\"range\":\"less\",\"value\":[3,1]}]");
            Assert.That(progress.Update(new DanSongStats { Bad = 2 }, 0), Is.Empty);
            Assert.That(progress.View(0).Counter, Is.EqualTo(1), "less shows what is left.");
            Assert.That(progress.Update(new DanSongStats { Bad = 3 }, 0), Is.EqualTo(new[] { 0 }));
            Assert.That(progress.IsFailed(0), Is.True);
            Assert.That(progress.Update(new DanSongStats { Bad = 4 }, 0), Is.Empty, "An exam fails once.");
        }

        [Test]
        public void CourseWideMoreExamsAreJudgedAtTheEndOfTheCourse()
        {
            var progress = Progress("[{\"type\":\"good\",\"range\":\"more\",\"value\":[250,300]}]");
            progress.Update(new DanSongStats { Good = 50 }, 0);
            Assert.That(progress.EndSong(), Is.Empty, "Not judged before the last song.");
            progress.Next();
            progress.Update(new DanSongStats { Good = 100 }, 0);
            Assert.That(progress.EndSong(), Is.Empty);
            progress.Next();
            Assert.That(progress.Total.Good, Is.EqualTo(150));
            progress.Update(new DanSongStats { Good = 90 }, 0);
            Assert.That(progress.EndSong(), Is.EqualTo(new[] { 0 }), "240 < 250 at the end of the course.");
            Assert.That(progress.Verdict(), Is.EqualTo(0));
        }

        [Test]
        public void PerSongExamsAreJudgedAtEachSongsEndAndSkipSongsWithoutABorder()
        {
            var progress = Progress("[{\"type\":\"combo\",\"range\":\"more\",\"value\":[[50],null,[80]]}]");
            progress.Update(new DanSongStats { MaxCombo = 60 }, 0);
            Assert.That(progress.EndSong(), Is.Empty);
            progress.Next();
            progress.Update(new DanSongStats { MaxCombo = 1 }, 0);
            Assert.That(progress.EndSong(), Is.Empty, "The second song has no border.");
            progress.Next();
            progress.Update(new DanSongStats { MaxCombo = 70 }, 0);
            Assert.That(progress.View(0).Red, Is.EqualTo(80));
            Assert.That(progress.View(0).SongCounter.Take(3), Is.EqualTo(new[] { 60, 0, 70 }), "A song without a border shows nothing.");
            Assert.That(progress.EndSong(), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void TheVerdictTakesTheWorstExamAndTheCleanliness()
        {
            DanProgress Play(DanSongStats[] songs, int gauge)
            {
                var p = Progress("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[80,100]},{\"type\":\"bad\",\"range\":\"less\",\"value\":[[5,1],[5,1],[5,1]]}]");
                for (int i = 0; i < songs.Length; i++)
                {
                    p.Update(songs[i], gauge);
                    p.EndSong();
                    if (i < songs.Length - 1) p.Next();
                }
                return p;
            }
            var clean = new DanSongStats { Good = 100 };
            Assert.That(Play(new[] { clean, clean, clean }, 100).Verdict(), Is.EqualTo(6), "Gold, no 可 and no 不可.");
            Assert.That(Play(new[] { clean, new DanSongStats { Good = 99, Ok = 1 }, clean }, 100).Verdict(), Is.EqualTo(5));
            Assert.That(Play(new[] { clean, new DanSongStats { Good = 99, Bad = 1 }, clean }, 100).Verdict(), Is.EqualTo(1),
                "One 不可 is red for that song's exam (gold is under 1) and a bad run.");
            Assert.That(Play(new[] { clean, clean, clean }, 90).Verdict(), Is.EqualTo(3), "A red gauge caps the course at red.");
            Assert.That(Play(new[] { clean, clean, clean }, 79).Verdict(), Is.EqualTo(0));
            Assert.That(Play(new[] { clean, clean, clean }, 100).Verdict(skipped: true), Is.EqualTo(0));
        }

        [Test]
        public void ACourseEndedEarlyOrWithoutExamsCannotPass()
        {
            var early = Progress("[{\"type\":\"bad\",\"range\":\"less\",\"value\":[[1],[9],[9]]}]");
            early.Update(new DanSongStats { Good = 99, Bad = 1 }, 100);
            Assert.That(early.EndSong(), Is.Empty, "Already failed during the song.");
            Assert.That(early.AnyFailed, Is.True);
            Assert.That(early.Verdict(), Is.EqualTo(0), "The course stopped before its last song.");
            var none = new DanProgress(Parse(Songs(1), "[]"), new[] { 100 });
            none.Update(new DanSongStats { Good = 100 }, 100);
            Assert.That(none.Verdict(), Is.EqualTo(0));
        }

        [Test]
        public void TheRunningComboAndScoreSumOverSongs()
        {
            var progress = Progress("[{\"type\":\"score\",\"range\":\"more\",\"value\":[1000]},{\"type\":\"hits\",\"range\":\"more\",\"value\":[10]}]", 2);
            progress.Update(new DanSongStats { Good = 3, Ok = 1, Rolls = 2, Score = 400, MaxCombo = 4 }, 0);
            progress.EndSong(); progress.Next();
            progress.Update(new DanSongStats { Good = 2, Score = 700, MaxCombo = 6 }, 0);
            Assert.That(progress.Value(progress.Course.Exams[0], -1), Is.EqualTo(1100));
            Assert.That(progress.Value(progress.Course.Exams[1], -1), Is.EqualTo(8));
            Assert.That(progress.Total.MaxCombo, Is.EqualTo(6));
            Assert.That(progress.RemainingNotes, Is.EqualTo(200 - 6));
        }

        [Test]
        public void ValuesOfTheWrongTypeAreFormatErrorsAndNumbersAreAccepted()
        {
            foreach (string extra in new[] { ",\"color\":[3]", ",\"format\":{}" })
                Assert.Throws<FormatException>(() => DanCourse.Parse(Course(Songs(1), extra: extra)), extra);
            Assert.Throws<FormatException>(() => DanCourse.Parse(Course("[{\"chart\":{\"sha256\":\"" + Hash1 + "\"},\"hidden\":{}}]")));
            Assert.Throws<FormatException>(() => Parse(Songs(1), "[{\"type\":\"score\",\"range\":\"more\",\"value\":[3000000000]}]"));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Songs(1), "[{\"type\":\"bad\",\"range\":\"less\",\"value\":[0]}]")).Message,
                Does.Contain("at least 1"), "A less border of 0 would fail before the first note.");
            var numeric = DanCourse.Parse(Course("[{\"chart\":{\"id\":42,\"course\":2}}]"), "https://example.test");
            Assert.That(numeric.Songs[0].Id, Is.EqualTo("42"));
            Assert.That(numeric.Songs[0].Difficulty, Is.EqualTo(Difficulty.Hard));
        }

        [Test]
        public void TheGaugeGetsACanonicalCourseName()
        {
            var ura = Song("Ura", Hash1, "COURSE:4\nLEVEL:10\n#START\n1111,\n#END");
            var oni = Song("Numbered", Hash2, "COURSE:3\nLEVEL:9\n#START\n1111,\n#END");
            var entry = new DanCourseResolver(new[] { ura, oni }, null).Resolve(DanCourse.Parse(Course(
                "[{\"chart\":{\"sha256\":\"" + Hash1 + "\",\"course\":\"Edit\"}},{\"chart\":{\"sha256\":\"" + Hash2 + "\"}}]")));
            Assert.That(entry.IsPlayable, Is.True, entry.Error);
            Assert.That(entry.Songs.Select(s => s.Course), Is.EqualTo(new[] { "4", "3" }), "TjaParser gets the COURSE as written.");
            Assert.That(entry.Songs.Select(s => s.GaugeCourse), Is.EqualTo(new[] { "Edit", "Oni" }));
            Assert.DoesNotThrow(() => SoulGauge.Dan(entry.Songs.Select(s => (s.GaugeCourse, s.Level)).ToList(), 8));
        }

        [Test]
        public void ACourseWideLessExamCountsOverEverySong()
        {
            var progress = Progress("[{\"type\":\"bad\",\"range\":\"less\",\"value\":[5,2]}]");
            progress.Update(new DanSongStats { Bad = 3 }, 0);
            progress.EndSong(); progress.Next();
            Assert.That(progress.View(0).Counter, Is.EqualTo(2));
            Assert.That(progress.View(0).Gold, Is.EqualTo(2));
            Assert.That(progress.Update(new DanSongStats { Bad = 2 }, 0), Is.EqualTo(new[] { 0 }), "3 + 2 uses up the 5.");
            Assert.That(progress.EndSong(), Is.Empty);
            Assert.That(progress.IsEnded, Is.True, "A failed exam ends the course after its song.");
            Assert.Throws<InvalidOperationException>(() => progress.Next());
            Assert.That(progress.Verdict(), Is.EqualTo(0));
        }

        [Test]
        public void TheVerdictWaitsForTheEndAndTheViewTellsASongWithoutABorder()
        {
            var progress = Progress("[{\"type\":\"ok\",\"range\":\"less\",\"value\":[[9],null,[9]]}]");
            progress.Update(new DanSongStats { Good = 100 }, 100);
            Assert.Throws<InvalidOperationException>(() => progress.Verdict(), "Mid-course there is no verdict yet.");
            Assert.That(progress.Verdict(skipped: true), Is.EqualTo(0));
            Assert.That(progress.View(0).HasBorder, Is.True);
            progress.EndSong(); progress.Next();
            Assert.That(progress.View(0).HasBorder, Is.False);
            progress.Update(new DanSongStats { Good = 99, Ok = 1 }, 100);
            progress.EndSong(); progress.Next();
            progress.Update(new DanSongStats { Good = 100 }, 100);
            progress.EndSong();
            Assert.That(progress.IsEnded, Is.True);
            Assert.Throws<InvalidOperationException>(() => progress.EndSong());
            Assert.That(progress.Verdict(), Is.EqualTo(5), "Gold, with one 可 on the song that had no border.");
        }

        [Test]
        public void LocalSongsAreFoundByHashThenByTitle()
        {
            var first = Song("First", Hash1);
            var second = Song("Second", "");
            var resolver = new DanCourseResolver(new[] { first, second }, null);
            var entry = resolver.Resolve(DanCourse.Parse(Course("[{\"chart\":{\"sha256\":\"" + Hash1.ToUpperInvariant() + "\"}},"
                + "{\"chart\":{\"sha256\":\"" + Hash2 + "\"},\"title\":\"Second\"}]")));
            Assert.That(entry.IsPlayable, Is.True, entry.Error);
            Assert.That(entry.Songs.Select(s => s.Song), Is.EqualTo(new[] { first, second }));
            Assert.That(entry.Songs[0].Course, Is.EqualTo("Oni"));
            Assert.That(entry.Songs[0].Level, Is.EqualTo(10));

            var missing = resolver.Resolve(DanCourse.Parse(Course("[{\"chart\":{\"sha256\":\"" + Hash2 + "\"},\"title\":\"Nowhere\"}]")));
            Assert.That(missing.Error, Is.EqualTo("Song not found: Nowhere"));
            Assert.That(missing.Songs, Is.Empty);
            var noCourse = resolver.Resolve(DanCourse.Parse(Course("[{\"chart\":{\"sha256\":\"" + Hash1 + "\",\"course\":\"Easy\"},\"title\":\"First\"}]")));
            Assert.That(noCourse.Error, Is.EqualTo("No Easy chart: First"));
        }

        [Test]
        public void ServerSongsAreFoundByIdOnTheirServer()
        {
            var song = Song("Online", "");
            song.chart = null;
            song.onlineChart = new Online.FanmadeChart { Server = "endpoint", Id = "42", TjaHash = Hash1 };
            song.onlineChart.Difficulties[(int)Difficulty.Oni] = new Online.FanmadeDifficulty { Course = "Oni", Level = 9, Id = 7 };
            var asked = new List<(string, string)>();
            var resolver = new DanCourseResolver(Array.Empty<SongDefinition>(), (server, id) => { asked.Add((server, id)); return id == "42" ? song : null; });
            DanCourseEntry Resolve(string chart) => resolver.Resolve(DanCourse.Parse(Course("[{\"chart\":" + chart + "}]"), "https://example.test"));

            var found = Resolve("{\"id\":\"42\",\"tjaHash\":\"" + Hash1 + "\"}");
            Assert.That(found.IsPlayable, Is.True, found.Error);
            Assert.That(found.Songs[0].Level, Is.EqualTo(9));
            Assert.That(asked[0], Is.EqualTo(("https://example.test", "42")));
            Assert.That(Resolve("{\"id\":\"43\"}").Error, Does.StartWith("Song not found"));
            Assert.That(Resolve("{\"id\":\"42\",\"tjaHash\":\"" + Hash2 + "\"}").Error, Does.StartWith("Song has changed"));
            Assert.That(Resolve("{\"id\":\"42\",\"course\":\"Hard\"}").Error, Does.StartWith("No Hard chart"));
            var offline = new DanCourseResolver(Array.Empty<SongDefinition>(), null);
            Assert.That(offline.Resolve(DanCourse.Parse(Course("[{\"chart\":{\"id\":\"42\"}}]"), "https://example.test")).Error,
                Does.StartWith("Song not found"), "Not signed in to the server: nothing is found.");
        }

        [Test]
        public void TheLoaderReadsLocalFoldersAndTheServerStandIn()
        {
            string root = Path.Combine(Path.GetTempPath(), "OurTaikoDanTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string dan = Path.Combine(root, DanCourseLoader.LocalFolder);
                Directory.CreateDirectory(Path.Combine(dan, "sub"));
                File.WriteAllText(Path.Combine(dan, "a.otdan.json"), Course(Songs(1)));
                File.WriteAllText(Path.Combine(dan, "sub", "broken.otdan.json"), "{");
                File.WriteAllText(Path.Combine(dan, "ignored.json"), Course(Songs(1)));
                var resolver = new DanCourseResolver(new[] { Song("First", Hash1) }, (s, id) => null);
                var local = DanCourseLoader.LoadLocal(new[] { root, Path.Combine(root, "missing") }, resolver);
                Assert.That(local.Select(e => Path.GetFileName(e.Path)), Is.EqualTo(new[] { "a.otdan.json", "broken.otdan.json" }));
                Assert.That(local[0].IsPlayable, Is.True, local[0].Error);
                Assert.That(local[1].IsPlayable, Is.False);
                Assert.That(local[1].Course.Title("en"), Is.EqualTo("broken"));

                string test = Path.Combine(root, DanCourseLoader.ServerTestFolder);
                Directory.CreateDirectory(test);
                File.WriteAllText(Path.Combine(test, "server.json"), "{\"server\":\"https://example.test/\",\"dans\":["
                    + Course("[{\"chart\":{\"id\":\"42\"}}]").Replace("local:test", "2026-10-21") + ",{\"id\":\"bad\"},"
                    + Course("[{\"chart\":{\"id\":\"42\"}}]", extra: ",\"color\":{}") + "]}");
                File.WriteAllText(Path.Combine(test, "noserver.json"), "{\"server\":{},\"dans\":[]}");
                var served = DanCourseLoader.LoadServerTest(test, resolver);
                Assert.That(served.Count, Is.EqualTo(4));
                Assert.That(served[0].Error, Does.Contain("server"), "noserver.json sorts first.");
                served.RemoveAt(0);
                Assert.That(served[0].Course.Id, Is.EqualTo("2026-10-21"), "A date-like id stays a string.");
                Assert.That(served[2].Error, Does.Contain("wrong type"));
                Assert.That(served.All(e => e.Course.Server == "https://example.test"), Is.True, "The trailing slash is dropped.");
                Assert.That(served[0].Error, Does.StartWith("Song not found"));
                Assert.That(served[1].Error, Does.Contain("title"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
