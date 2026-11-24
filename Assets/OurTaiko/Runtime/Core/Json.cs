using System.Linq;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // Reading the JSON a server answers with: every value is checked, and a wrong one throws
    // OnlineException with the API_* code the screens show.
    public static class Json
    {
        public static JObject Parse(string text)
        {
            try
            {
                using var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(text))
                    { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
                return JToken.ReadFrom(reader) as JObject ?? throw new OnlineException("API_JSON_INVALID");
            }
            catch (Newtonsoft.Json.JsonException error) { throw new OnlineException("API_JSON_INVALID", error); }
        }

        // A string a server may leave out (one older than the field): "" then, and for anything else than a string.
        public static string Optional(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            return value != null && value.Type == JTokenType.String ? ((string)value).Trim() : "";
        }

        public static string Str(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.String) throw new OnlineException("API_STRING_INVALID");
            return (string)value;
        }

        public static long Number(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.Integer || (long)value < 0) throw new OnlineException("API_NUMBER_INVALID");
            return (long)value;
        }

        public static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

        // An ID a server gives as a string or as a whole number, as text.
        public static string Id(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value?.Type == JTokenType.Integer && (long)value > 0) return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Str(v, key);
        }

        // Fanmade charts have 32 hex digits; OurTaiko Database charts are positive whole numbers.
        public static bool ChartId(string s) => HexId(s, 32)
            || (s.Length > 0 && s.Length <= 18 && s[0] != '0' && s.All(c => c >= '0' && c <= '9'));

        public static bool HexId(string s, int length) => s.Length == length && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}
