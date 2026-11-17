using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Writes small .otanim files for tests, from the format document (SPEC.md in the otanim folder
    // beside this repository) rather than from the game's reader: a 4×4 texture (left half red,
    // right half blue) and one square part covering it, 100 units wide, in clips that move it.
    public static class OtanimFixture
    {
        public const float Unit = 1f / 16;

        // A clip: its name, loop or not, and each frame's square as its left x (bottom 0, side 100).
        public struct Clip
        {
            public string Name;
            public bool Loop;
            public float Fps;
            public float[] Lefts;
            public int LoopStart;
            public byte Alpha;
        }

        public static Clip Make(string name, bool loop, float fps, params float[] lefts) =>
            new Clip { Name = name, Loop = loop, Fps = fps, Lefts = lefts, Alpha = 255 };

        public static byte[] Build(params Clip[] clips)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < 16; i++) pixels[i] = i % 4 < 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            texture.SetPixels32(pixels);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            var file = new MemoryStream();
            var w = new BinaryWriter(file);
            w.Write(Encoding.ASCII.GetBytes("OTAN"));
            w.Write((ushort)1);
            w.Write((ushort)0);

            var info = new BinaryWriter(new MemoryStream());
            foreach (float v in new[] { -10f, -10f, 210f, 110f }) info.Write(v);
            info.Write(Unit);
            info.Write((ushort)1);
            info.Write((ushort)clips.Length);
            WriteString(info, "fixture");
            Chunk(w, "INFO", ((MemoryStream)info.BaseStream).ToArray());
            Chunk(w, "TEXR", png);

            // The square: bottom left, top left, top right, bottom right; uv v down from the top.
            var part = new BinaryWriter(new MemoryStream());
            WriteString(part, "square");
            part.Write((byte)0);
            part.Write((ushort)4);
            part.Write((ushort)6);
            foreach (var uv in new[] { (0f, 1f), (0f, 0f), (1f, 0f), (1f, 1f) })
            {
                part.Write((ushort)Mathf.RoundToInt(uv.Item1 * 65535));
                part.Write((ushort)Mathf.RoundToInt(uv.Item2 * 65535));
            }
            foreach (ushort i in new ushort[] { 0, 1, 2, 2, 3, 0 }) part.Write(i);
            Chunk(w, "PART", Deflate(((MemoryStream)part.BaseStream).ToArray()));

            foreach (var clip in clips)
            {
                var c = new BinaryWriter(new MemoryStream());
                WriteString(c, clip.Name);
                c.Write(clip.Fps);
                c.Write((byte)(clip.Loop ? 1 : 0));
                c.Write((uint)clip.Lefts.Length);
                c.Write((uint)clip.LoopStart);
                short[] previous = null;
                foreach (float left in clip.Lefts)
                {
                    c.Write((ushort)1);
                    c.Write((ushort)0);
                    var q = new List<short>();
                    foreach (var (x, y) in new[] { (left, 0f), (left, 100f), (left + 100, 100f), (left + 100, 0f) })
                    {
                        q.Add((short)Mathf.RoundToInt(x / Unit));
                        q.Add((short)Mathf.RoundToInt(y / Unit));
                    }
                    c.Write((byte)(previous != null ? 1 : 0));
                    c.Write(new byte[] { 255, 255, 255, clip.Alpha });
                    for (int i = 0; i < q.Count; i++) c.Write(previous != null ? (short)(q[i] - previous[i]) : q[i]);
                    previous = q.ToArray();
                }
                Chunk(w, "CLIP", Deflate(((MemoryStream)c.BaseStream).ToArray()));
            }
            Chunk(w, "XTRA", new byte[] { 1, 2, 3 }); // a chunk a reader does not know is skipped
            return file.ToArray();
        }

        public static string WriteFile(string name, byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(), "OurTaikoDonTests", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static void WriteString(BinaryWriter w, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        static void Chunk(BinaryWriter w, string tag, byte[] payload)
        {
            w.Write(Encoding.ASCII.GetBytes(tag));
            w.Write((uint)payload.Length);
            w.Write(payload);
        }

        static byte[] Deflate(byte[] data)
        {
            var output = new MemoryStream();
            using (var z = new DeflateStream(output, CompressionMode.Compress, true)) z.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }
}
