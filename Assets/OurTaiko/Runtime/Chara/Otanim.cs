using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace OurTaiko.Chara
{
    // An .otanim file: baked 2D cut-out animation (spine2otanim, in the otanim folder beside this
    // repository, makes them from Spine). One
    // texture, parts (triangles with fixed texture coordinates) and clips whose frames give every
    // drawn part's vertex positions. Format: SPEC.md in that otanim folder, whose Go package
    // (ourtaiko.org/otanim) is its reference.
    public sealed class Otanim
    {
        public const int Version = 1;
        public static readonly byte[] Magic = { (byte)'O', (byte)'T', (byte)'A', (byte)'N' };

        public enum Blend : byte { Normal = 0, Additive = 1, Multiply = 2, Screen = 3 }

        public sealed class Part
        {
            public string Name;
            public Blend Blend;
            public Vector2[] UVs; // 0..1, v down from the image's top
            public int[] Indices;
        }

        public struct Draw
        {
            public int Part;
            public Color32 Color;
            public int Offset; // into Clip.Positions, 2 floats per vertex
        }

        public sealed class Frame
        {
            public Draw[] Draws;
        }

        public sealed class Clip
        {
            public string Name;
            public float Fps;
            public bool Loop;
            // The frame a loop goes back to: the frames before it are an intro played once.
            public int LoopStart;
            public Frame[] Frames;
            public float[] Positions; // every frame's vertices, x, y in units (y up)

            // A loop's frames cover [0, n/fps) the first time, then wrap to LoopStart; a one-shot's
            // last frame is its end.
            public float Duration => Loop ? Frames.Length / Fps : Math.Max(0, Frames.Length - 1) / Fps;
        }

        public string Name;
        public bool Premultiplied;
        public Rect View; // units, y up
        public float Unit;
        public byte[] Texture; // PNG
        public Part[] Parts;
        public Clip[] Clips;

        public static bool IsOtanim(byte[] bytes) =>
            bytes != null && bytes.Length >= 4 && bytes[0] == Magic[0] && bytes[1] == Magic[1] && bytes[2] == Magic[2] && bytes[3] == Magic[3];

        public Clip Find(string name)
        {
            foreach (var c in Clips) if (c.Name == name) return c;
            return null;
        }

        sealed class Reader
        {
            readonly byte[] b;
            int p;
            public Reader(byte[] bytes) { b = bytes; }
            public bool AtEnd => p >= b.Length;
            void Need(int n) { if (p + n > b.Length) throw new InvalidDataException("the otanim file ends early"); }
            public byte U8() { Need(1); return b[p++]; }
            public ushort U16() { Need(2); var v = (ushort)(b[p] | b[p + 1] << 8); p += 2; return v; }
            public short I16() => (short)U16();
            public uint U32() { Need(4); var v = (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24); p += 4; return v; }
            public float F32() => BitConverter.Int32BitsToSingle((int)U32());
            public byte[] Bytes(int n) { Need(n); var v = new byte[n]; Buffer.BlockCopy(b, p, v, 0, n); p += n; return v; }
            public string Str() => Encoding.UTF8.GetString(Bytes(U16()));
        }

        static byte[] Inflate(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var z = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        public static Otanim Parse(byte[] bytes)
        {
            if (!IsOtanim(bytes) || bytes.Length < 8) throw new InvalidDataException("not an otanim file");
            var head = new Reader(bytes);
            head.Bytes(4);
            int version = head.U16();
            if (version != Version) throw new InvalidDataException("otanim version " + version + " is not supported");
            var file = new Otanim { Premultiplied = (head.U16() & 1) != 0 };
            int partCount = -1, clipCount = 0;
            var clips = new List<Clip>();
            while (!head.AtEnd)
            {
                string tag = Encoding.ASCII.GetString(head.Bytes(4));
                byte[] payload = head.Bytes((int)head.U32());
                switch (tag)
                {
                    case "INFO":
                    {
                        var r = new Reader(payload);
                        float x0 = r.F32(), y0 = r.F32(), x1 = r.F32(), y1 = r.F32();
                        file.View = Rect.MinMaxRect(x0, y0, x1, y1);
                        file.Unit = r.F32();
                        partCount = r.U16();
                        clipCount = r.U16();
                        file.Name = r.Str();
                        break;
                    }
                    case "TEXR":
                        file.Texture = payload;
                        break;
                    case "PART":
                    {
                        if (partCount < 0) throw new InvalidDataException("PART before INFO");
                        var r = new Reader(Inflate(payload));
                        file.Parts = new Part[partCount];
                        for (int i = 0; i < partCount; i++)
                        {
                            var part = new Part { Name = r.Str(), Blend = (Blend)r.U8() };
                            int vc = r.U16(), ic = r.U16();
                            part.UVs = new Vector2[vc];
                            for (int j = 0; j < vc; j++) part.UVs[j] = new Vector2(r.U16() / 65535f, r.U16() / 65535f);
                            part.Indices = new int[ic];
                            for (int j = 0; j < ic; j++)
                            {
                                part.Indices[j] = r.U16();
                                if (part.Indices[j] >= vc) throw new InvalidDataException("part " + part.Name + " indexes past its vertices");
                            }
                            file.Parts[i] = part;
                        }
                        break;
                    }
                    case "CLIP":
                        if (file.Parts == null) throw new InvalidDataException("CLIP before PART");
                        clips.Add(ReadClip(new Reader(Inflate(payload)), file));
                        break;
                    // Unknown chunks are skipped: later versions may add some.
                }
            }
            if (file.Texture == null || file.Parts == null || clips.Count != clipCount || clipCount == 0)
                throw new InvalidDataException("the otanim file is missing INFO, TEXR, PART or a CLIP");
            if (file.View.width <= 0 || file.View.height <= 0) throw new InvalidDataException("the otanim view is empty");
            file.Clips = clips.ToArray();
            return file;
        }

        static Clip ReadClip(Reader r, Otanim file)
        {
            var clip = new Clip { Name = r.Str(), Fps = r.F32() };
            clip.Loop = (r.U8() & 1) != 0;
            uint frameCount = r.U32();
            uint loopStart = r.U32();
            if (frameCount == 0 || frameCount > 1_000_000 || !(clip.Fps > 0)) throw new InvalidDataException("clip " + clip.Name + " has no frames or no rate");
            if (loopStart >= frameCount || (!clip.Loop && loopStart != 0)) throw new InvalidDataException("clip " + clip.Name + " loops back to a frame it does not have");
            clip.LoopStart = (int)loopStart;
            clip.Frames = new Frame[frameCount];
            var positions = new List<float>();
            var previous = new Dictionary<int, short[]>();
            for (int f = 0; f < frameCount; f++)
            {
                var draws = new Draw[r.U16()];
                var now = new Dictionary<int, short[]>();
                for (int d = 0; d < draws.Length; d++)
                {
                    int part = r.U16();
                    if (part >= file.Parts.Length) throw new InvalidDataException("clip " + clip.Name + " draws a part that is not there");
                    bool delta = (r.U8() & 1) != 0;
                    var color = new Color32(r.U8(), r.U8(), r.U8(), r.U8());
                    int n = file.Parts[part].UVs.Length * 2;
                    short[] before = null;
                    if (delta && !previous.TryGetValue(part, out before))
                        throw new InvalidDataException("clip " + clip.Name + " frame " + f + " is a change of a part the frame before does not draw");
                    var q = new short[n];
                    draws[d] = new Draw { Part = part, Color = color, Offset = positions.Count };
                    for (int i = 0; i < n; i++)
                    {
                        q[i] = r.I16();
                        if (delta) q[i] = unchecked((short)(q[i] + before[i]));
                        positions.Add(q[i] * file.Unit);
                    }
                    now[part] = q;
                }
                previous = now;
                clip.Frames[f] = new Frame { Draws = draws };
            }
            clip.Positions = positions.ToArray();
            return clip;
        }

        // The frame at t seconds into the clip and how far toward the next (0..1); next is -1 when
        // there is none to ease toward (a one-shot's end).
        public static void FrameAt(Clip clip, double seconds, out int frame, out int next, out float toward)
        {
            int n = clip.Frames.Length;
            double at = Math.Max(0, seconds) * clip.Fps;
            if (clip.Loop)
            {
                // The intro (frames before LoopStart) once, then LoopStart..n-1 over and over.
                int start = clip.LoopStart;
                if (at >= n) at = start + (at - start) % (n - start);
                frame = Math.Min(n - 1, (int)at);
                next = frame + 1 < n ? frame + 1 : start;
            }
            else
            {
                frame = Math.Min(n - 1, (int)at);
                next = frame + 1 < n ? frame + 1 : -1;
            }
            toward = next < 0 ? 0 : (float)(at - Math.Floor(at));
        }

        // How many times the clip has come round by `seconds`: a loop's first pass (its intro and
        // its loop) is 0, each loop after it one more; a one-shot plays whole each time.
        public static int Pass(Clip clip, double seconds)
        {
            int n = clip.Frames.Length;
            double at = Math.Max(0, seconds) * clip.Fps;
            if (clip.Loop) return at < n ? 0 : 1 + (int)Math.Floor((at - n) / (n - clip.LoopStart));
            return n > 1 ? (int)Math.Floor(at / (n - 1)) : (int)Math.Floor(at);
        }

        // Writes the clip at t seconds as triangles into a mesh fitting `box` (layout units, y up),
        // keeping the view's aspect; frames are eased into each other where the same part is in
        // both. Additive parts get 2 added to u (the shader takes it back off).
        public void Sample(Clip clip, double seconds, Rect box, bool mirror, Color32 tint, List<UIVertex> vertices, List<int> indices)
        {
            FrameAt(clip, seconds, out int f, out int nf, out float t);
            var frame = clip.Frames[f];
            var next = nf >= 0 && t > 0 ? clip.Frames[nf] : null;
            float scale = Mathf.Min(box.width / View.width, box.height / View.height);
            float ox = box.center.x - View.center.x * scale;
            float oy = box.center.y - View.center.y * scale;
            float cx = box.center.x;
            foreach (var draw in frame.Draws)
            {
                var part = Parts[draw.Part];
                int nextOffset = -1;
                Color32 nextColor = draw.Color;
                if (next != null)
                    foreach (var d in next.Draws)
                        if (d.Part == draw.Part) { nextOffset = d.Offset; nextColor = d.Color; break; }
                var c = nextOffset >= 0 ? Color32.Lerp(draw.Color, nextColor, t) : draw.Color;
                c = new Color32((byte)(c.r * tint.r / 255), (byte)(c.g * tint.g / 255), (byte)(c.b * tint.b / 255), (byte)(c.a * tint.a / 255));
                int start = vertices.Count;
                float uShift = part.Blend == Blend.Additive ? 2f : 0f;
                for (int i = 0; i < part.UVs.Length; i++)
                {
                    float x = clip.Positions[draw.Offset + 2 * i], y = clip.Positions[draw.Offset + 2 * i + 1];
                    if (nextOffset >= 0)
                    {
                        x += (clip.Positions[nextOffset + 2 * i] - x) * t;
                        y += (clip.Positions[nextOffset + 2 * i + 1] - y) * t;
                    }
                    x = ox + x * scale;
                    if (mirror) x = 2 * cx - x;
                    var uv = part.UVs[i];
                    vertices.Add(new UIVertex
                    {
                        position = new Vector3(x, oy + y * scale),
                        color = c,
                        uv0 = new Vector4(uv.x + uShift, 1 - uv.y),
                    });
                }
                foreach (int i in part.Indices) indices.Add(start + i);
            }
        }
    }
}
