using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko.Chara
{
    // The parts of glTF 2.0 binary (.glb) the Don models use: the JSON chunk, the BIN chunk, and
    // accessors read out as floats or ints. Everything stays in glTF's own space (right-handed,
    // +Y up, UV origin at the top left); Chara3D draws in that space, as YataiDON does.
    public sealed class Glb
    {
        public readonly JObject Json;
        readonly byte[] data;
        readonly int binStart;

        Glb(JObject json, byte[] data, int binStart)
        {
            Json = json; this.data = data; this.binStart = binStart;
        }

        public static Glb Parse(byte[] data)
        {
            if (data == null || data.Length < 20) throw new FormatException("glb: too short");
            if (BitConverter.ToUInt32(data, 0) != 0x46546C67u) throw new FormatException("glb: bad magic");
            int jsonLength = BitConverter.ToInt32(data, 12);
            if (BitConverter.ToUInt32(data, 16) != 0x4E4F534Au || 20 + jsonLength > data.Length) throw new FormatException("glb: no JSON chunk");
            var json = JObject.Parse(Encoding.UTF8.GetString(data, 20, jsonLength));
            int bin = -1;
            int next = 20 + jsonLength;
            if (next + 8 <= data.Length && BitConverter.ToUInt32(data, next + 4) == 0x004E4942u) bin = next + 8;
            return new Glb(json, data, bin);
        }

        public JArray Array(string name) => Json[name] as JArray ?? new JArray();

        static int Components(string type) => type switch
        {
            "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16,
            _ => throw new FormatException("glb: accessor type " + type),
        };

        static int ComponentSize(int componentType) => componentType switch
        {
            5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
            _ => throw new FormatException("glb: component type " + componentType),
        };

        // Where an accessor's elements are: first byte, stride, count, components per element.
        (int start, int stride, int count, int width, int componentType, bool normalized) Layout(int accessor)
        {
            if (binStart < 0) throw new FormatException("glb: no BIN chunk");
            var a = (JObject)Array("accessors")[accessor];
            int componentType = (int)a["componentType"];
            int width = Components((string)a["type"]);
            int count = (int)a["count"];
            var view = (JObject)Array("bufferViews")[(int)a["bufferView"]];
            int start = binStart + ((int?)view["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
            int stride = (int?)view["byteStride"] ?? width * ComponentSize(componentType);
            return (start, stride, count, width, componentType, (bool?)a["normalized"] ?? false);
        }

        public int Count(int accessor) => (int)((JObject)Array("accessors")[accessor])["count"];

        // Every component as a float; normalized integers come out in [0, 1] (or [-1, 1]).
        public float[] Floats(int accessor)
        {
            var (start, stride, count, width, type, normalized) = Layout(accessor);
            var result = new float[count * width];
            int size = ComponentSize(type);
            for (int i = 0; i < count; i++)
            for (int c = 0; c < width; c++)
            {
                int at = start + i * stride + c * size;
                float value = type switch
                {
                    5126 => BitConverter.ToSingle(data, at),
                    5121 => normalized ? data[at] / 255f : data[at],
                    5120 => normalized ? Math.Max((sbyte)data[at] / 127f, -1f) : (sbyte)data[at],
                    5123 => normalized ? BitConverter.ToUInt16(data, at) / 65535f : BitConverter.ToUInt16(data, at),
                    5122 => normalized ? Math.Max(BitConverter.ToInt16(data, at) / 32767f, -1f) : BitConverter.ToInt16(data, at),
                    _ => BitConverter.ToUInt32(data, at),
                };
                result[i * width + c] = value;
            }
            return result;
        }

        public int[] Ints(int accessor)
        {
            var (start, stride, count, width, type, _) = Layout(accessor);
            var result = new int[count * width];
            int size = ComponentSize(type);
            for (int i = 0; i < count; i++)
            for (int c = 0; c < width; c++)
            {
                int at = start + i * stride + c * size;
                result[i * width + c] = type switch
                {
                    5121 => data[at],
                    5120 => (sbyte)data[at],
                    5123 => BitConverter.ToUInt16(data, at),
                    5122 => BitConverter.ToInt16(data, at),
                    5125 => (int)BitConverter.ToUInt32(data, at),
                    _ => (int)BitConverter.ToSingle(data, at),
                };
            }
            return result;
        }

        // An image's encoded bytes (PNG/JPEG) from its buffer view.
        public byte[] ImageBytes(int image)
        {
            var img = (JObject)Array("images")[image];
            if (img["bufferView"] == null || binStart < 0) return null;
            var view = (JObject)Array("bufferViews")[(int)img["bufferView"]];
            int start = binStart + ((int?)view["byteOffset"] ?? 0);
            int length = (int)view["byteLength"];
            var bytes = new byte[length];
            Buffer.BlockCopy(data, start, bytes, 0, length);
            return bytes;
        }

        // A node's local transform (matrix, or translation/rotation/scale).
        public static Matrix4x4 NodeMatrix(JObject node)
        {
            if (node["matrix"] is JArray m && m.Count == 16)
            {
                var r = new Matrix4x4();
                for (int i = 0; i < 16; i++) r[i % 4, i / 4] = (float)m[i];
                return r;
            }
            var (t, q, s) = NodeTrs(node);
            return Matrix4x4.TRS(t, q, s);
        }

        public static (Vector3 t, Quaternion r, Vector3 s) NodeTrs(JObject node)
        {
            var t = node["translation"] is JArray tr ? new Vector3((float)tr[0], (float)tr[1], (float)tr[2]) : Vector3.zero;
            var r = node["rotation"] is JArray ro ? new Quaternion((float)ro[0], (float)ro[1], (float)ro[2], (float)ro[3]) : Quaternion.identity;
            var s = node["scale"] is JArray sc ? new Vector3((float)sc[0], (float)sc[1], (float)sc[2]) : Vector3.one;
            return (t, r, s);
        }

        // A column-major glTF matrix at element i of a MAT4 accessor's floats.
        public static Matrix4x4 Matrix(float[] values, int i)
        {
            var m = new Matrix4x4();
            for (int k = 0; k < 16; k++) m[k % 4, k / 4] = values[i * 16 + k];
            return m;
        }
    }
}
