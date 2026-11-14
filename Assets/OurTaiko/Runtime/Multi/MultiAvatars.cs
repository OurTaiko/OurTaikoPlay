using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko.Multi
{
    // Players' account pictures for マルチ演奏: fetched once by address, decoded (the account center
    // serves WebP; unity.webp reads it) and kept as round sprites. Get answers at once, with the
    // picture when it is here and null while it is on its way or when there is none; Loaded says
    // when one arrived, for whoever drew a placeholder meanwhile.
    public static class MultiAvatars
    {
        // The side of a kept picture, the most bytes a picture may have, and how many are kept.
        public const int Size = 128;
        public const long ByteLimit = 2 * 1024 * 1024;
        public const int Kept = 96;
        const int SourceLimit = 2048;

        public static event Action Loaded;
        // Fetches a picture's bytes. Tests may put their own here.
        public static Func<string, CancellationToken, Task<byte[]>> Fetch { get; set; } = FetchAsync;

        static readonly Dictionary<string, Sprite> ready = new Dictionary<string, Sprite>();
        static readonly List<string> order = new List<string>();
        // Asked for, and either still coming or not to be had.
        static readonly HashSet<string> asked = new HashSet<string>();
        static HttpClient http;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ready.Clear(); order.Clear(); asked.Clear();
            Loaded = null; Fetch = FetchAsync;
        }

        // The picture at an address, or null: not here yet, none to be had, or no address.
        public static Sprite Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (ready.TryGetValue(url, out var sprite) && sprite != null) return sprite;
            if (asked.Add(url)) _ = LoadAsync(url);
            return null;
        }

        // Only what a browser would call secure, or this machine (a development server).
        public static bool IsAllowed(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        }

        static async Task LoadAsync(string url)
        {
            try
            {
                if (!IsAllowed(url)) return;
                byte[] bytes = await Fetch(url, CancellationToken.None);
                var sprite = Round(Decode(bytes));
                if (sprite == null) return;
                ready[url] = sprite;
                order.Add(url);
                // The oldest make room; a screen still showing one draws its placeholder next time.
                while (order.Count > Kept)
                {
                    if (ready.TryGetValue(order[0], out var old) && old != null)
                    {
                        UnityEngine.Object.Destroy(old.texture);
                        UnityEngine.Object.Destroy(old);
                    }
                    ready.Remove(order[0]); asked.Remove(order[0]);
                    order.RemoveAt(0);
                }
                Loaded?.Invoke();
            }
            catch (Exception error)
            {
                // A player without a picture that can be shown keeps the placeholder.
                Log.Debug("Multi", "Avatar not shown: " + error.Message);
            }
        }

        static async Task<byte[]> FetchAsync(string url, CancellationToken cancel)
        {
            http ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) throw new IOException("HTTP " + (int)response.StatusCode);
            if ((response.Content.Headers.ContentLength ?? 0) > ByteLimit) throw new IOException("too large");
            using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[32768];
            var output = new MemoryStream();
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancel);
                if (read == 0) break;
                if (output.Length + read > ByteLimit) throw new IOException("too large");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        // The picture's pixels: WebP by its header, anything else as Unity reads images (PNG, JPEG).
        static Texture2D Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return null;
            bool webp = bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P';
            if (webp)
            {
                WebP.Texture2DExt.GetWebPDimensions(bytes, out int width, out int height);
                if (width <= 0 || height <= 0 || width > SourceLimit || height > SourceLimit) return null;
                byte[] pixels = WebP.Texture2DExt.LoadRGBAFromWebP(bytes, ref width, ref height, false, out WebP.Error error);
                if (error != WebP.Error.Success || pixels == null) return null;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, false);
                return texture;
            }
            var other = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (other.LoadImage(bytes, false) && other.width <= SourceLimit && other.height <= SourceLimit) return other;
            UnityEngine.Object.Destroy(other);
            return null;
        }

        // The middle square of a picture, scaled to Size and cut to a disc with a soft edge. The
        // source is destroyed.
        static Sprite Round(Texture2D source)
        {
            if (source == null) return null;
            var from = source.GetPixels32();
            int width = source.width, height = source.height, side = Math.Min(width, height);
            int left = (width - side) / 2, bottom = (height - side) / 2;
            UnityEngine.Object.Destroy(source);
            var to = new Color32[Size * Size];
            float step = (float)side / Size, radius = Size / 2f;
            // Shrinking averages the source pixels under each new one; enlarging takes the nearest.
            int span = Math.Max(1, Mathf.CeilToInt(step));
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x + 0.5f - radius, dy = y + 0.5f - radius;
                    float cover = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    if (cover <= 0) continue;
                    int sx = Math.Min(side - 1, (int)(x * step)), sy = Math.Min(side - 1, (int)(y * step));
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int v = sy; v < Math.Min(side, sy + span); v++)
                        for (int u = sx; u < Math.Min(side, sx + span); u++)
                        {
                            var c = from[(bottom + v) * width + left + u];
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    to[y * Size + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n * cover));
                }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(to);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
