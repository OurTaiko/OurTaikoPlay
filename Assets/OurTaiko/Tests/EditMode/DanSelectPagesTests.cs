using System.Collections.Generic;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class DanSelectPagesTests
    {
        const string Fanmade = "https://fanmade.ourtaiko.org", Database = "https://database.ourtaiko.org";

        static DanCourseEntry Entry(string id, string server, bool official = true, string error = null)
        {
            var course = new DanCourse { Id = id, Server = server, Official = official };
            course.Titles[""] = id;
            return new DanCourseEntry { Course = course, Path = id, Error = error };
        }

        [Test]
        public void ThreePagesLocalThenEachServersOfficialCoursesAndTheGaidenEntry()
        {
            var pages = DanSelectPages.Build(
                new[] { Entry("local-a", null, false), Entry("local-b", null, false, "broken") },
                new[] { Entry("f-official", Fanmade + "/"), Entry("f-community", Fanmade, false), Entry("d-official", Database), Entry("f-broken", Fanmade, true, "bad"), Entry("x-official", "https://other.example") },
                new[] { Entry("stand-in", "https://other.example", false) }, Fanmade, Database);
            Assert.That(pages.Count, Is.EqualTo(3));
            Assert.That(pages[0].Title, Is.EqualTo("ローカル"));
            Assert.That(pages[0].IsLocal, Is.True);
            CollectionAssert.AreEqual(new[] { "local-a", "local-b" }, Ids(pages[0]));
            // Community courses, broken ones and the stand-in are not listed; the 外伝 entry closes the page.
            CollectionAssert.AreEqual(new[] { "f-official", "x-official", "stand-in", "gaiden" }, Ids(pages[1]));
            CollectionAssert.AreEqual(new[] { "d-official", "gaiden" }, Ids(pages[2]));
            var gaiden = pages[2].Courses[1];
            Assert.That(gaiden.IsGaiden, Is.True);
            Assert.That(gaiden.IsPlayable, Is.True, "the 外伝 chip is not greyed");
            Assert.That(gaiden.Course.Color, Is.EqualTo(6));
            Assert.That(gaiden.Course.Server, Is.EqualTo(Database));
            Assert.That(gaiden.Course.Title("ja"), Is.EqualTo("外伝"));
            Assert.That(pages[2].GaidenIndex, Is.EqualTo(1));
        }

        [Test]
        public void ACodesCourseJoinsBeforeTheGaidenEntryOrReplacesItsListedSelf()
        {
            var pages = DanSelectPages.Build(new DanCourseEntry[0], new[] { Entry("10", Fanmade) }, new DanCourseEntry[0], Fanmade, Database);
            var page = pages[1];
            Assert.That(DanSelectPages.Insert(page, Entry("77", Fanmade, false)), Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "10", "77", "gaiden" }, Ids(page));
            var fresh = Entry("10", Fanmade);
            Assert.That(DanSelectPages.Insert(page, fresh), Is.EqualTo(0));
            Assert.That(page.Courses[0], Is.SameAs(fresh));
            Assert.That(page.Courses.Count, Is.EqualTo(3));
        }

        [Test]
        public void CodesAreSixLettersOrDigitsUpperCased()
        {
            Assert.That(DanSelectPages.CleanCode(" ab12cd "), Is.EqualTo("AB12CD"));
            Assert.That(DanSelectPages.CleanCode("AB-12-CD"), Is.EqualTo("AB12CD"));
            Assert.That(DanSelectPages.CleanCode("AB12C"), Is.Null);
            Assert.That(DanSelectPages.CleanCode("AB12CDE"), Is.Null);
            Assert.That(DanSelectPages.CleanCode("ＡＢ１２ＣＤ"), Is.Null);
            Assert.That(DanSelectPages.CleanCode(null), Is.Null);
        }

        [Test]
        public void AServersCourseReadsItsOfficialFlagAndCode()
        {
            var course = DanCourse.Parse("{\"format\":1,\"id\":\"5\",\"title\":{\"ja\":\"初段\"},\"official\":true,\"code\":\"ab12cd\",\"songs\":[{\"chart\":{\"id\":\"1\",\"course\":\"Oni\"}}],\"exams\":[]}", Fanmade);
            Assert.That(course.Official, Is.True);
            Assert.That(course.Code, Is.EqualTo("AB12CD"));
            var local = DanCourse.Parse("{\"format\":1,\"id\":\"x\",\"title\":\"十段\",\"songs\":[{\"chart\":{\"course\":\"Oni\"},\"title\":\"a\"}],\"exams\":[]}");
            Assert.That(local.Official, Is.False);
            Assert.That(local.Code, Is.EqualTo(""));
        }

        static List<string> Ids(DanSelectPage page) => page.Courses.ConvertAll(c => c.Course.Id);
    }
}
