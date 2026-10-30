using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        // Resource manifests are fetched on each selection; only verified content is cached.
        // Never construct a URL from the catalog's bucket key or forward API credentials.
        // Without the server, the preview excerpt cached while online plays (never the full audio).
        public async Task<string> PreparePreviewAsync(FanmadeChart selected, CancellationToken cancel = default)
        {
            var endpoint = Endpoint(selected.Server);
            if (endpoint == null)
            {
                var cached = Offline.Get(selected.Server, selected.Id) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
                if (cached.PreviewHash == null) throw new FanmadeException("PREVIEW_UNAVAILABLE");
                return await CachedObjectAsync(cached.PreviewHash, cached.PreviewSize, cancel, new FileProgress(), () => { });
            }
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var manifest = await endpoint.ResourcesAsync(selected.Id, cancel);
                    var resource = manifest.Preview;
                    if (resource == null) throw new FanmadeException("PREVIEW_UNAVAILABLE");
                    try
                    {
                        string path = await EnsureObjectAsync(endpoint, resource, cancel);
                        cancel.ThrowIfCancellationRequested();
                        return path;
                    }
                    catch (HttpStatusException e) when (attempt == 0 && (e.Status == 403 || e.Status == 404)) { }
                }
            }
            finally { endpoint.Transport.Release(); }
            throw new FanmadeException("PREVIEW_UNAVAILABLE");
        }
    }
}
