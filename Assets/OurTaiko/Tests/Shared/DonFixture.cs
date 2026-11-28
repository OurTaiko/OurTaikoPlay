using System;

namespace OurTaiko.Tests
{
    // Files for the 3D Don's tests that are not in the game (it only has the plain Don).
    public static class DonFixture
    {
        // A background as a downloaded file would be: one two-sided white wall wider and taller
        // than a Don's area, at depth z of the Don's own space (+z is toward the viewer), with no skin.
        // The camera sees 0.5 m either side of the Don's feet in depth, less what the turned wall takes: keep |z| to 0.3.
        public static string WallBackground(string name, float z)
        {
            var positions = new[] { -0.5f, -0.3f, z, 0.5f, -0.3f, z, 0.5f, 0.9f, z, -0.5f, 0.9f, z };
            var normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f };
            var indices = new uint[] { 0, 1, 2, 2, 3, 0 };
            var bin = new byte[48 + 48 + 24];
            Buffer.BlockCopy(positions, 0, bin, 0, 48);
            Buffer.BlockCopy(normals, 0, bin, 48, 48);
            Buffer.BlockCopy(indices, 0, bin, 96, 24);
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],"
                + "\"buffers\":[{\"byteLength\":120}],"
                + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":48,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":96,\"byteLength\":24}],"
                + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"},"
                + "{\"bufferView\":2,\"componentType\":5125,\"count\":6,\"type\":\"SCALAR\"}],"
                + "\"materials\":[{\"name\":\"wall_CULLNONE\",\"extras\":{\"shaderType\":\"taikoEffect\"}}],"
                + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"indices\":2,\"material\":0,\"mode\":4}]}]}";
            var text = System.Text.Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OurTaikoDonTests", name + ".glb");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using (var file = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                file.Write(0x46546C67u); file.Write(2u); file.Write((uint)(12 + 8 + text.Length + 8 + bin.Length));
                file.Write((uint)text.Length); file.Write(0x4E4F534Au); file.Write(text);
                file.Write((uint)bin.Length); file.Write(0x004E4942u); file.Write(bin);
            }
            return path;
        }
    }
}
