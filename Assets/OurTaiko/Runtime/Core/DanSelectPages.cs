using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // One page of the 段位道場 select: ローカル (every local course), Fanmade and Database (the
    // official courses each server lists, then the 外伝 entry that takes a course code).
    public sealed class DanSelectPage
    {
        public string Title;
        // Null for the local page; otherwise the base URL whose courses it shows.
        public string Server;
        public readonly List<DanCourseEntry> Courses = new List<DanCourseEntry>();

        public bool IsLocal => Server == null;
        public int GaidenIndex => Courses.FindIndex(c => c.IsGaiden);
    }

    public static class DanSelectPages
    {
        public const string LocalTitle = "ローカル", FanmadeTitle = "Fanmade", DatabaseTitle = "Database";
        public const string GaidenTitle = "外伝", GaidenId = "gaiden";
        // box/folder 6: the green plaque.
        public const int GaidenColor = 6;

        // local: the courses of the song library roots. servers: every connected server's courses
        // (their Course.Server names the server). standIns: the DanServerTest stand-in courses,
        // listed unfiltered on the page of their server (Fanmade when it is none of the two).
        public static List<DanSelectPage> Build(IEnumerable<DanCourseEntry> local, IEnumerable<DanCourseEntry> servers,
            IEnumerable<DanCourseEntry> standIns, string fanmadeUrl, string databaseUrl)
        {
            var pages = new List<DanSelectPage>
            {
                new DanSelectPage { Title = LocalTitle },
                new DanSelectPage { Title = FanmadeTitle, Server = Trim(fanmadeUrl) },
                new DanSelectPage { Title = DatabaseTitle, Server = Trim(databaseUrl) },
            };
            pages[0].Courses.AddRange(local);
            // Only official courses are listed; the rest are reached by code. A course that did
            // not parse cannot say whether it is official and is left out.
            foreach (var entry in servers)
                if (entry.IsPlayable ? entry.Course.Official : false) PageOf(pages, entry.Course.Server).Courses.Add(entry);
            foreach (var entry in standIns) PageOf(pages, entry.Course.Server).Courses.Add(entry);
            for (int i = 1; i < pages.Count; i++) pages[i].Courses.Add(Gaiden(pages[i].Server));
            return pages;
        }

        static DanSelectPage PageOf(List<DanSelectPage> pages, string server)
        {
            string url = Trim(server);
            return string.Equals(url, pages[2].Server, StringComparison.OrdinalIgnoreCase) ? pages[2] : pages[1];
        }

        static string Trim(string url) => (url ?? "").Trim().TrimEnd('/');

        public static DanCourseEntry Gaiden(string server)
        {
            var course = new DanCourse { Id = GaidenId, Server = Trim(server), Color = GaidenColor };
            course.Titles[""] = GaidenTitle;
            return new DanCourseEntry { Course = course, Path = Trim(server) + "/api/v1/game/dans/{code}", IsGaiden = true };
        }

        // A course a code fetched joins its page before the 外伝 entry, or replaces the listed one
        // with its id. Returns its index.
        public static int Insert(DanSelectPage page, DanCourseEntry entry)
        {
            int existing = page.Courses.FindIndex(c => !c.IsGaiden && c.Course.Id == entry.Course.Id);
            if (existing >= 0) { page.Courses[existing] = entry; return existing; }
            int at = page.GaidenIndex;
            if (at < 0) at = page.Courses.Count;
            page.Courses.Insert(at, entry);
            return at;
        }

        // A code as typed: six capital letters or digits, or null.
        public static string CleanCode(string typed)
        {
            string code = new string((typed ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return code.Length == 6 && code.All(c => c < 128) ? code : null;
        }
    }
}
