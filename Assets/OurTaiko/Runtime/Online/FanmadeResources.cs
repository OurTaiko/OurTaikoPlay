using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed class FanmadeResource
    {
        public string Url, HeadUrl, Hash, ContentType;
        public long Size;

        // One entry of a resource manifest: where to download a file of at most `limit` bytes and
        // how to verify it.
        public static FanmadeResource From(JObject item, long limit, Action<string> validateUrl)
        {
            var r = new FanmadeResource { Url = Json.Str(item, "url"), HeadUrl = Json.Str(item, "headUrl"),
                Hash = Json.Str(item, "sha256"), ContentType = Json.Str(item, "contentType"), Size = Json.Number(item, "size") };
            if (!Json.HexId(r.Hash, 64) || r.Size <= 0 || r.Size > limit) throw new FanmadeException("RESOURCE_METADATA_INVALID");
            validateUrl(r.Url); validateUrl(r.HeadUrl);
            return r;
        }
    }

    public sealed class FanmadeResources
    {
        // Movie: the optional background video (BGMOVIE), an H.264 MP4; null when there is none.
        public FanmadeResource Tja, Audio, Preview, Movie;
        public const long MovieLimit = 128L * 1024 * 1024;
        public static FanmadeResources Parse(JObject value, string chartId, Action<string> validateUrl)
        {
            if (Json.Id(value, "chartId") != chartId) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
            if (!(value["resources"] is JObject resources)) throw new FanmadeException("RESOURCE_MANIFEST_INVALID");
            FanmadeResource Read(string kind, long limit) =>
                FanmadeResource.From(resources[kind] as JObject ?? throw new FanmadeException("RESOURCE_MISSING"), limit, validateUrl);
            var result = new FanmadeResources { Tja = Read("tja", 4L * 1024 * 1024), Audio = Read("audio", 256L * 1024 * 1024) };
            if (result.Audio.ContentType != "audio/ogg" && result.Audio.ContentType != "audio/mpeg") throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            if (resources["preview"] != null) {
                result.Preview = Read("preview", 32L * 1024 * 1024);
                if (result.Preview.ContentType != "audio/ogg") throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            }
            // The chart plays without a movie of another type.
            if (resources["movie"] != null) {
                result.Movie = Read("movie", MovieLimit);
                if (result.Movie.ContentType != "video/mp4") result.Movie = null;
            }
            return result;
        }
    }

    public sealed partial class FanmadeEndpoint
    {
        public void ValidateResourceUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new FanmadeException("RESOURCE_URL_INVALID");
            if (uri.Scheme == "https") return;
#if UNITY_INCLUDE_TESTS
            // A local test API may hand out plain-HTTP links to itself or another local fixture.
            if (uri.Scheme == "http" && uri.IsLoopback && Uri.TryCreate(Config.baseUrl, UriKind.Absolute, out var api) && api.IsLoopback) return;
#endif
            throw new FanmadeException("RESOURCE_URL_INVALID");
        }
        public async Task<FanmadeResources> ResourcesAsync(string chartId, CancellationToken cancel = default)
        {
            if (!Json.ChartId(chartId)) throw new FanmadeException("API_ID_INVALID");
            return FanmadeResources.Parse(Json.Parse(await AuthorizedAsync("/api/v1/charts/" + chartId + "/resources", cancel: cancel)), chartId, ValidateResourceUrl);
        }
    }
}
