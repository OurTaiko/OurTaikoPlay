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
    // serves WebP; unity.webp reads it) and kept in two shapes: a disc for the room's cards and
    // ranking, and the whole square for the play scene's scoreboard, which lays it behind a card.
    // Get and GetSquare answer at once, with the picture when it is here and null while it is on
    // its way or when there is none; Loaded says when one arrived, for whoever drew a placeholder
    // meanwhile.
    public static class MultiAvatars
    {
        // The sides of a kept picture's disc and square, the most bytes a picture may have, and
        // how many pictures are kept.
        public const int Size = 128, SquareSize = 256;
        public const long ByteLimit = 2 * 1024 * 1024;
        public const int Kept = 96;
        const int SourceLimit = 2048;

        public static event Action Loaded;
        // Fetches a picture's bytes. Tests may put their own here.
        public static Func<string, CancellationToken, Task<byte[]>> Fetch { get; set; } = FetchAsync;

        static readonly Dictionary<string, (Sprite Disc, Sprite Square)> ready = new Dictionary<string, (Sprite, Sprite)>();
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

        // The picture at an address as a disc, or null: not here yet, none to be had, or no address.
        public static Sprite Get(string url) => Find(url).Disc;
        // The same picture uncut: its middle square.
        public static Sprite GetSquare(string url) => Find(url).Square;

        static (Sprite Disc, Sprite Square) Find(string url)
        {
            if (string.IsNullOrEmpty(url)) return default;
            if (ready.TryGetValue(url, out var kept) && kept.Disc != null) return kept;
            if (asked.Add(url)) _ = LoadAsync(url);
            return default;
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
                var source = Online.Pictures.Decode(bytes, SourceLimit);
                if (source == null) return;
                var pixels = source.GetPixels32();
                int width = source.width, height = source.height;
                UnityEngine.Object.Destroy(source);
                ready[url] = (Shape(pixels, width, height, Size, true), Shape(pixels, width, height, SquareSize, false));
                order.Add(url);
                // The oldest make room; a screen still showing one draws its placeholder next time.
                while (order.Count > Kept)
                {
                    if (ready.TryGetValue(order[0], out var old))
                        foreach (var sprite in new[] { old.Disc, old.Square })
                            if (sprite != null) { UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite); }
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

        // The middle square of a picture, scaled to `size`; `disc` cuts it to a disc with a soft edge.
        static Sprite Shape(Color32[] from, int width, int height, int size, bool disc)
        {
            int side = Math.Min(width, height);
            int left = (width - side) / 2, bottom = (height - side) / 2;
            var to = new Color32[size * size];
            float step = (float)side / size, radius = size / 2f;
            // Shrinking averages the source pixels under each new one; enlarging takes the nearest.
            int span = Math.Max(1, Mathf.CeilToInt(step));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius, dy = y + 0.5f - radius;
                    float cover = disc ? Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy)) : 1;
                    if (cover <= 0) continue;
                    int sx = Math.Min(side - 1, (int)(x * step)), sy = Math.Min(side - 1, (int)(y * step));
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int v = sy; v < Math.Min(side, sy + span); v++)
                        for (int u = sx; u < Math.Min(side, sx + span); u++)
                        {
                            var c = from[(bottom + v) * width + left + u];
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    to[y * size + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n * cover));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(to);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
