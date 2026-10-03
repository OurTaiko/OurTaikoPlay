using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // Thrown with OurTaikoPlayer's error codes (API_*, NETWORK_*, HTTP_<status>, DOWNLOAD_*), which
    // the login and loading screens show as they are.
    public class FanmadeException : Exception
    {
        public FanmadeException(string code, Exception inner = null) : base(code, inner) { }
    }

    public sealed class HttpStatusException : FanmadeException
    {
        public readonly int Status;
        public HttpStatusException(int status) : base("HTTP_" + status) { Status = status; }
    }

    public sealed class FanmadeDifficulty
    {
        public string Course = "", Player = "";
        public int Level, BlockIndex;
        // cloudScoreEligible: only these blocks take part in online scores.
        public bool Cloud;
    }

    // A published chart as /api/v1/game/categories/{id}/charts and /api/v1/charts/{id} return it.
    public sealed class FanmadeChart
    {
        public static readonly string[] Courses = { "Easy", "Normal", "Hard", "Oni", "Edit" };

        public string Server = "", Id = "", Version = "", Title = "", Subtitle = "", Maker = "";
        public string TjaHash = "", AudioHash = "", Encoding = "", AudioName = "";
        public string Category = "", Genre = "";
        public readonly Dictionary<string, string> Titles = new Dictionary<string, string>(), Subtitles = new Dictionary<string, string>();
        public double Bpm = 120, DemoStart;
        // One slot per Easy..Edit; a cloud-eligible block wins over a DOUBLE-only one.
        public readonly FanmadeDifficulty[] Difficulties = new FanmadeDifficulty[5];
        public readonly List<FanmadeDifficulty> Blocks = new List<FanmadeDifficulty>();

        public bool IsPlayable => Difficulties.Any(d => d != null);

        public static FanmadeChart From(JToken v, string server)
        {
            var c = new FanmadeChart
            {
                Server = server, Id = Json.Str(v, "id"), Version = Json.Str(v, "versionId"), Title = Json.Str(v, "title"),
                Subtitle = Json.Str(v, "subtitle"), Maker = Json.Str(v, "maker"), TjaHash = Json.Str(v, "tjaHash"),
                AudioHash = Json.Str(v, "audioHash"), Encoding = Json.Str(v, "encoding"), AudioName = Json.Str(v, "audioName"),
            };
            if (!Json.HexId(c.Id, 32) || !Json.HexId(c.Version, 32) || !Json.HexId(c.TjaHash, 64) || !Json.HexId(c.AudioHash, 64))
                throw new FanmadeException("API_ID_INVALID");
            c.Titles["en"] = c.Title; c.Subtitles["en"] = c.Subtitle;
            foreach (var (key, target) in new[] { ("titleTranslations", c.Titles), ("subtitleTranslations", c.Subtitles) })
            {
                if (!(v[key] is JObject translations)) throw new FanmadeException("API_TRANSLATIONS_INVALID");
                foreach (var pair in translations)
                    if (pair.Value.Type == JTokenType.String) target[pair.Key] = (string)pair.Value;
            }
            if (!Json.IsNumber(v["bpm"]) || !Json.IsNumber(v["demoStart"])) throw new FanmadeException("API_METADATA_INVALID");
            c.Bpm = (double)v["bpm"]; c.DemoStart = (double)v["demoStart"];
            if (!(v["difficulties"] is JArray difficulties)) throw new FanmadeException("API_DIFFICULTIES_INVALID");
            foreach (var d in difficulties)
            {
                string name = Json.Str(d, "course");
                int slot = Array.IndexOf(Courses, name);
                if (slot < 0) continue;  // Tower/Dan are not playable here.
                if (d["cloudScoreEligible"]?.Type != JTokenType.Boolean) throw new FanmadeException("API_DIFFICULTY_INVALID");
                long level = Json.Number(d, "level"), block = Json.Number(d, "blockIndex");
                if (level > 100 || block > 100000) throw new FanmadeException("API_DIFFICULTY_INVALID");
                var difficulty = new FanmadeDifficulty
                {
                    Course = name, Level = (int)level, BlockIndex = (int)block,
                    Cloud = (bool)d["cloudScoreEligible"], Player = Json.Str(d, "player"),
                };
                c.Blocks.Add(difficulty);
                if (c.Difficulties[slot] == null || (!c.Difficulties[slot].Cloud && difficulty.Cloud)) c.Difficulties[slot] = difficulty;
            }
            return c;
        }

        // The verified audio keeps its real container in the cache name; anything else is refused.
        public string CachedAudioName
        {
            get
            {
                string extension = System.IO.Path.GetExtension(AudioName).ToUpperInvariant();
                if (extension == ".MP3") return "audio.mp3";
                if (extension == ".OGG") return "audio.ogg";
                throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            }
        }

        // MAKER and the TITLE/SUBTITLE translations the playable copy and the catalog entry use.
        public string TitleHeaders()
        {
            var output = new System.Text.StringBuilder("MAKER:" + LineText(Maker) + "\n");
            foreach (var (key, values) in new[] { ("TITLE", Titles), ("SUBTITLE", Subtitles) })
                foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (pair.Key != "en" && pair.Key != "ja" && pair.Key != "zh" && pair.Key != "ko") continue;
                    output.Append(key).Append(pair.Key == "en" ? "" : pair.Key.ToUpperInvariant()).Append(':').Append(LineText(pair.Value)).Append('\n');
                }
            return output.ToString();
        }

        // Song-select metadata only (no notes): it is never played, the download replaces it.
        public string CatalogTja()
        {
            var text = new System.Text.StringBuilder("// Fanmade catalog metadata only. Never play this file.\n");
            text.Append(TitleHeaders());
            text.Append("BPM:").Append(Bpm.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("DEMOSTART:").Append(DemoStart.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var d in Difficulties)
                if (d != null) text.Append("COURSE:").Append(d.Course).Append("\nLEVEL:").Append(d.Level).Append("\n#START\n0,\n#END\n");
            return text.ToString();
        }

        internal static string LineText(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\0', ' ');
    }

    // One server category in bootstrap order, with its charts in API order. A chart listed in
    // several categories is the same FanmadeChart (by id) in each.
    public sealed class FanmadeCategory
    {
        public string Server = "", ServerName = "", Id = "", Title = "", Genre = "";
        public readonly List<string> ChartIds = new List<string>();
    }

    public sealed class FanmadeScore
    {
        public string Id = "", Song = "", Version = "", Difficulty = "";
        public long Good, Ok, Bad, Score, Drumroll, MaxCombo;

        public static FanmadeScore From(JToken v) => new FanmadeScore
        {
            Id = Json.Str(v, "id"), Song = Json.Str(v, "songId"), Version = Json.Str(v, "versionId"), Difficulty = Json.Str(v, "difficulty"),
            Good = Json.Number(v, "good"), Ok = Json.Number(v, "ok"), Bad = Json.Number(v, "bad"), Score = Json.Number(v, "score"),
            Drumroll = Json.Number(v, "drumroll"), MaxCombo = Json.Number(v, "max_combo"),
        };
    }

    // Recorded input sent with a score when the server declares scoreReplayVersion 1.
    // Types: 0 left ka, 1 left don, 2 right don, 3 right ka; times are judged game time in ms.
    public sealed class PlayRecord
    {
        public const int MaxInputs = 100000;
        public const double MaxTimeMs = 86400000;
        public int AudioOffsetMs, VisualOffsetMs;
        public readonly List<(double Ms, int Type)> Inputs = new List<(double, int)>();

        public static int TypeOf(bool isKa, bool right) => isKa ? (right ? 3 : 0) : (right ? 2 : 1);

        // null (sent as JSON null) when the record is too long or holds an invalid event.
        public JToken ToJson()
        {
            if (Inputs.Count > MaxInputs) return JValue.CreateNull();
            var inputs = new JArray();
            foreach (var (ms, type) in Inputs)
            {
                if (double.IsNaN(ms) || double.IsInfinity(ms) || Math.Abs(ms) > MaxTimeMs || type < 0 || type > 3) return JValue.CreateNull();
                inputs.Add(new JArray(ms, type));
            }
            return new JObject
            {
                ["version"] = 1, ["audio_offset_ms"] = AudioOffsetMs, ["visual_offset_ms"] = VisualOffsetMs, ["inputs"] = inputs,
            };
        }
    }

    public sealed class FileProgress
    {
        public enum State { Waiting, Downloading, Verifying, Cached, Complete }
        public State Status;
        public long Received;
        public long Total;  // 0: the server sent no length.
        public FileProgress Clone() => (FileProgress)MemberwiseClone();
    }

    public sealed class DownloadProgress
    {
        public enum Stage { Checking, Files, Preparing, Ready }
        public Stage Step;
        public FileProgress Chart = new FileProgress(), Audio = new FileProgress();
        public DownloadProgress Clone() => new DownloadProgress { Step = Step, Chart = Chart.Clone(), Audio = Audio.Clone() };
    }

    static class Json
    {
        public static JObject Parse(string text)
        {
            try { return JToken.Parse(text) as JObject ?? throw new FanmadeException("API_JSON_INVALID"); }
            catch (Newtonsoft.Json.JsonException error) { throw new FanmadeException("API_JSON_INVALID", error); }
        }

        public static string Str(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.String) throw new FanmadeException("API_STRING_INVALID");
            return (string)value;
        }

        public static long Number(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.Integer || (long)value < 0) throw new FanmadeException("API_NUMBER_INVALID");
            return (long)value;
        }

        public static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

        public static bool HexId(string s, int length) => s.Length == length && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}
