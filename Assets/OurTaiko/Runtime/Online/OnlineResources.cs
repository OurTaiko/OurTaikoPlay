using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed class OnlineResource
    {
        // Path is under the distribution root (Distribution), as version 2 of the API gives it.
        public string Path, Hash, ContentType;
        public long Size;

        // One entry of a resource manifest: which file of at most `limit` bytes to download and
        // how to verify it.
        public static OnlineResource From(JObject item, long limit)
        {
            var r = new OnlineResource { Path = Json.Str(item, "path"), Hash = Json.Str(item, "sha256"),
                ContentType = Json.Str(item, "contentType"), Size = Json.Number(item, "size") };
            if (!Json.HexId(r.Hash, 64) || r.Size <= 0 || r.Size > limit) throw new OnlineException("RESOURCE_METADATA_INVALID");
            if (!Distribution.ValidPath(r.Path)) throw new OnlineException("RESOURCE_PATH_INVALID");
            return r;
        }
    }

    public sealed class OnlineResources
    {
        // Movie: the optional background video (BGMOVIE), an H.264 MP4; null when there is none.
        public OnlineResource Tja, Audio, Preview, Movie;
        public const long MovieLimit = 128L * 1024 * 1024;
        public static OnlineResources Parse(JObject value, string chartId)
        {
            if (Json.Id(value, "chartId") != chartId) throw new OnlineException("RESOURCE_CHART_MISMATCH");
            if (!(value["resources"] is JObject resources)) throw new OnlineException("RESOURCE_MANIFEST_INVALID");
            OnlineResource Read(string kind, long limit) =>
                OnlineResource.From(resources[kind] as JObject ?? throw new OnlineException("RESOURCE_MISSING"), limit);
            var result = new OnlineResources { Tja = Read("tja", 4L * 1024 * 1024), Audio = Read("audio", 256L * 1024 * 1024) };
            if (result.Audio.ContentType != "audio/ogg" && result.Audio.ContentType != "audio/mpeg") throw new OnlineException("API_AUDIO_FORMAT_UNSUPPORTED");
            if (resources["preview"] != null) {
                result.Preview = Read("preview", 32L * 1024 * 1024);
                if (result.Preview.ContentType != "audio/ogg") throw new OnlineException("API_AUDIO_FORMAT_UNSUPPORTED");
            }
            // The chart plays without a movie of another type.
            if (resources["movie"] != null) {
                result.Movie = Read("movie", MovieLimit);
                if (result.Movie.ContentType != "video/mp4") result.Movie = null;
            }
            return result;
        }
    }

    public sealed partial class OnlineEndpoint
    {
        // The root this server's files come from: the one its site (or, without one, the server
        // itself) names for this player (Distribution).
        public Task<FileRoot> FilesAsync(CancellationToken cancel = default)
            => Distribution.RootAsync(Config.site.Length > 0 ? Config.site : Config.baseUrl, cancel);

        public async Task<OnlineResources> ResourcesAsync(string chartId, CancellationToken cancel = default)
        {
            if (!Json.ChartId(chartId)) throw new OnlineException("API_ID_INVALID");
            return OnlineResources.Parse(Json.Parse(await AuthorizedAsync("/api/v2/charts/" + chartId + "/resources", cancel: cancel)), chartId);
        }
    }
}
