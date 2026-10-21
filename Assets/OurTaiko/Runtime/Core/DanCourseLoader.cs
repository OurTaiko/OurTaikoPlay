using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OurTaiko
{
    // Reads the course files: each song library root's Dan/*.otdan.json (local courses), and until
    // servers hand courses out, the stand-in for their answer in DanServerTest/*.json:
    // { "server": "<base URL>", "dans": [ <course>, ... ] }, the body GET /api/v1/game/dans is to
    // return plus the server it came from.
    public static class DanCourseLoader
    {
        public const string LocalFolder = "Dan", LocalPattern = "*.otdan.json", ServerTestFolder = "DanServerTest";

        public static List<DanCourseEntry> LoadLocal(IEnumerable<string> roots, DanCourseResolver resolver)
        {
            var entries = new List<DanCourseEntry>();
            foreach (string root in roots)
            {
                string folder = Path.Combine(root, LocalFolder);
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, LocalPattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    DanCourse course;
                    try { course = DanCourse.Parse(ReadText(file)); }
                    catch (Exception error) when (error is FormatException || error is IOException || error is UnauthorizedAccessException)
                    { entries.Add(Broken(file, error.Message)); continue; }
                    entries.Add(resolver.Resolve(course, file));
                }
            }
            return entries;
        }

        public static List<DanCourseEntry> LoadServerTest(string folder, DanCourseResolver resolver)
        {
            var entries = new List<DanCourseEntry>();
            if (!Directory.Exists(folder)) return entries;
            foreach (string file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                JObject root;
                try
                {
                    using var reader = new JsonTextReader(new StringReader(ReadText(file))) { DateParseHandling = DateParseHandling.None };
                    root = JObject.Load(reader);
                }
                catch (Exception error) when (error is JsonException || error is IOException || error is UnauthorizedAccessException)
                { entries.Add(Broken(file, error.Message)); continue; }
                string server = root["server"]?.Type == JTokenType.String ? ((string)root["server"]).Trim().TrimEnd('/') : "";
                if (server.Length == 0 || !(root["dans"] is JArray dans)) { entries.Add(Broken(file, "Needs 'server' and 'dans'.")); continue; }
                foreach (var item in dans)
                {
                    try { entries.Add(resolver.Resolve(DanCourse.From(item as JObject ?? throw new FormatException("A course must be an object."), server), file)); }
                    catch (FormatException error) { entries.Add(Broken(file, error.Message, server)); }
                }
            }
            return entries;
        }

        static string ReadText(string file) => LocalSongScanner.ReadText(File.ReadAllBytes(file));

        static DanCourseEntry Broken(string file, string error, string server = null)
        {
            string name = Path.GetFileName(file);
            var course = new DanCourse { Id = name, Server = server };
            course.Titles[""] = name.EndsWith(".otdan.json") ? name.Substring(0, name.Length - ".otdan.json".Length) : Path.GetFileNameWithoutExtension(name);
            return new DanCourseEntry { Course = course, Path = file, Error = error };
        }
    }
}
