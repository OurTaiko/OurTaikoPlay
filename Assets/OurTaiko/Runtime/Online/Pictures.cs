using System;
using UnityEngine;

namespace OurTaiko.Online
{
    // Pictures the servers send, as textures: WebP (playlist covers, account pictures), which
    // unity.webp decodes, or anything Unity reads itself (PNG, JPEG).
    public static class Pictures
    {
        // A WebP file by its RIFF header.
        public static bool IsWebP(byte[] bytes) => bytes != null && bytes.Length >= 16
            && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P';

        // `bytes` decoded, or null when they are not a picture or one wider or taller than `limit`
        // (a broken WebP throws). A WebP longer than `longest` on a side (0: any length) is scaled
        // down to it as it is decoded, its proportions kept. `readable` keeps the pixels for
        // GetPixels; otherwise they are only on the GPU.
        public static Texture2D Decode(byte[] bytes, int limit, int longest = 0, bool readable = true)
        {
            if (bytes == null || bytes.Length < 16) return null;
            if (IsWebP(bytes))
            {
                WebP.Texture2DExt.GetWebPDimensions(bytes, out int width, out int height);
                if (width <= 0 || height <= 0 || width > limit || height > limit) return null;
                WebP.Texture2DExt.ScalingFunction scale = null;
                if (longest > 0 && Math.Max(width, height) > longest)
                {
                    bool wide = width >= height;
                    int w = wide ? longest : Math.Max(1, (int)Math.Round((double)width * longest / height));
                    int h = wide ? Math.Max(1, (int)Math.Round((double)height * longest / width)) : longest;
                    scale = WebP.Texture2DExt.ScaleToSize(w, h);
                }
                byte[] pixels = WebP.Texture2DExt.LoadRGBAFromWebP(bytes, ref width, ref height, false, out WebP.Error error, scale);
                if (error != WebP.Error.Success || pixels == null) return null;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, !readable);
                return texture;
            }
            var other = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (other.LoadImage(bytes, !readable) && other.width <= limit && other.height <= limit) return other;
            if (Application.isPlaying) UnityEngine.Object.Destroy(other);
            else UnityEngine.Object.DestroyImmediate(other);
            return null;
        }
    }
}
