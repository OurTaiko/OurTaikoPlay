using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    // The cache's files by what is in them: <cache>/objects/<first two of the hash>/<SHA-256>.
    // Whatever is downloaded from the distribution root by size and hash is kept here (a Don's
    // files, the play backdrops), so one copy serves every thing that names it.
    public static class ObjectCache
    {
        public static string PathOf(string cacheRoot, string hash) => Path.Combine(cacheRoot, "objects", hash.Substring(0, 2), hash);

        public static bool ValidHash(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }

        public static string Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        // The file at path is the one of that size and hash.
        public static bool Matches(string path, long size, string hash)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == hash;
        }

        // How long a file's answer may take to begin; the body, however large, is bounded by the
        // caller's token only.
        static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(30);

        // One file's bytes from one address, checked against its size and hash; `received` is told
        // how many have come, as they come.
        public static async Task<byte[]> FetchAsync(string url, long size, string hash, Action<long> received, CancellationToken cancel)
        {
            received?.Invoke(0);
            using var response = await OurTaikoHttpClient.GetAsync(url, HeadersTimeout, cancel, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode + " for " + url);
            using var stream = await response.Content.ReadAsStreamAsync();
            var bytes = new MemoryStream((int)Math.Min(size, int.MaxValue));
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0)
            {
                if (bytes.Length + read > size) throw new InvalidDataException("the download of " + url + " is larger than its size");
                bytes.Write(buffer, 0, read);
                received?.Invoke(bytes.Length);
            }
            var data = bytes.ToArray();
            string got = await Task.Run(() => Sha256(data), cancel);
            if (data.LongLength != size || got != hash) throw new InvalidDataException("the download of " + url + " does not match its hash");
            return data;
        }

        // Written beside its place and moved in, so a reader never sees half a file.
        public static void Store(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path)) { }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
