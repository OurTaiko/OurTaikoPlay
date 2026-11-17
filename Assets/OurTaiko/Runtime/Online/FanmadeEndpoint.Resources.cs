using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeEndpoint
    {
        // No API credentials, cookies, login recovery or redirects on the resource channel.
        public async Task<byte[]> ResourceBytesAsync(FanmadeResource resource, CancellationToken cancel,
            Action<long, long> progress = null)
        {
            try { return await ResourceDownload.FetchAsync(http, resource, cancel, progress, ValidateResourceUrl); }
            catch (OperationCanceledException) { throw new FanmadeException(cancel.IsCancellationRequested ? "DOWNLOAD_CANCELLED" : "NETWORK_TIMEOUT"); }
            // Do not retain transport exceptions: their messages may contain signed URLs.
            catch (HttpRequestException error) { throw new FanmadeException(NetworkCode(error)); }
            catch (IOException error) { throw new FanmadeException(NetworkCode(error)); }
        }
    }
}
