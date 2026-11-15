using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko.Chara
{
    // One part of a Don (a costume, a head or a body) as its glb has it, shared by every Chara3D
    // wearing it: the primitives in bind pose, the skin, the materials sorted by how YataiDON draws
    // them, and the decoded textures. Read-only once loaded; Chara3D skins and recolours copies.
    public sealed class DonPart
    {
        public sealed class Primitive
        {
            public Vector3[] Positions, Normals;
            public Vector2[] Uv;      // flipped to Unity's bottom-left origin
            public float[] Thickness; // COLOR_0.g: the black line pass's per-vertex thickness
            public int[] Joints;      // 4 per vertex, into Skin
            public float[] Weights;   // 4 per vertex
            public int[] Indices;
            public int Material;
        }

        public sealed class MaterialInfo
        {
            public string Name;
            public int Image = -1;
            public Color Color = Color.white;
            public bool Recolor, Face, Additive, Cutout, Blend, TwoSided;
        }

        public readonly List<Primitive> Primitives = new List<Primitive>();
        public readonly List<MaterialInfo> Materials = new List<MaterialInfo>();
        public Texture2D[] Images = Array.Empty<Texture2D>();
        public string[] JointNames = Array.Empty<string>();
        public Matrix4x4[] InverseBind = Array.Empty<Matrix4x4>();
        public int FaceMaterial = -1;

        // head/body parts always get the standard 0.137 face plate; a costume keeps its own plate
        // (some are deliberately small) but never a larger one -- cos 30 (鏡もち) and cos 9 ship a
        // 0.18 plate that overflows the drum head (YataiDON Chara3D::load_part).
        public const float FacePlaneSize = 0.137f;

        public static DonPart Load(byte[] glbBytes, bool normalizeFace)
        {
            var glb = Glb.Parse(glbBytes);
            var part = new DonPart();
            part.ReadMaterials(glb);
            part.ReadImages(glb);
            part.ReadSkin(glb);
            part.ReadMeshes(glb);
            if (part.FaceMaterial >= 0) part.FitFacePlane(normalizeFace);
            return part;
        }

        void ReadMaterials(Glb glb)
        {
            var textures = glb.Array("textures");
            foreach (JObject m in glb.Array("materials"))
            {
                var info = new MaterialInfo { Name = (string)m["name"] ?? "" };
                var pbr = m["pbrMetallicRoughness"] as JObject;
                if (pbr?["baseColorTexture"] is JObject tex)
                {
                    var texture = (JObject)textures[(int)tex["index"]];
                    info.Image = (int?)texture["source"] ?? -1;
                }
                if (pbr?["baseColorFactor"] is JArray f && f.Count == 4)
                    info.Color = new Color((float)f[0], (float)f[1], (float)f[2], (float)f[3]);
                string shader = (string)m["extras"]?["shaderType"];
                if (shader == "taikoEffectChangeColors") info.Recolor = true;
                else if (shader == "taikoEffectFace" && FaceMaterial < 0) { info.Face = true; FaceMaterial = Materials.Count; }
                // YataiDON parse_glb_material_indices: the material names carry the cabinet's shader.
                string name = info.Name.ToLowerInvariant();
                bool cutout = name.Contains("_color_s_cus_") && !name.Contains("_a_ab");
                bool gltfBlend = (string)m["alphaMode"] == "BLEND";
                if (name.Contains("_aa_add")) info.Additive = true;
                else if (cutout) info.Cutout = true;
                // alpha-blended: _A_AB by name, or any other glTF BLEND material that is not an
                // alpha-tested one (e.g. a plain "lambert" glint sheet) -- drawn after the face
                if (name.Contains("_a_ab") || gltfBlend && !cutout) info.Blend = true;
                if (name.Contains("cullnone")) info.TwoSided = true;
                if (info.Face) { info.Additive = false; info.Blend = false; }
                if (info.Additive) info.Color = Color.white;
                Materials.Add(info);
            }
        }

        void ReadImages(Glb glb)
        {
            var images = glb.Array("images");
            Images = new Texture2D[images.Count];
            for (int i = 0; i < images.Count; i++)
            {
                var bytes = glb.ImageBytes(i);
                if (bytes == null) continue;
                // Kept readable: a recoloured Don reads its pixels (Chara3D.SetDonColors). With
                // mipmaps: a costume's 1024 px sheets are drawn a few hundred pixels tall.
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                decoded.LoadImage(bytes, false);
                var texture = new Texture2D(decoded.width, decoded.height, TextureFormat.RGBA32, true)
                {
                    name = "Don image " + i, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat,
                };
                texture.SetPixels32(decoded.GetPixels32());
                texture.Apply(true, false);
                UnityEngine.Object.DestroyImmediate(decoded);
                Images[i] = texture;
            }
        }

        void ReadSkin(Glb glb)
        {
            var skins = glb.Array("skins");
            if (skins.Count == 0) return;
            var skin = (JObject)skins[0];
            var nodes = glb.Array("nodes");
            var joints = (JArray)skin["joints"];
            JointNames = new string[joints.Count];
            InverseBind = new Matrix4x4[joints.Count];
            float[] ibm = skin["inverseBindMatrices"] != null ? glb.Floats((int)skin["inverseBindMatrices"]) : null;
            for (int j = 0; j < joints.Count; j++)
            {
                JointNames[j] = (string)((JObject)nodes[(int)joints[j]])["name"] ?? "";
                InverseBind[j] = ibm != null ? Glb.Matrix(ibm, j) : Matrix4x4.identity;
            }
        }

        void ReadMeshes(Glb glb)
        {
            // Every mesh node of the scene; a skinned mesh ignores its node's transform (glTF 2.0 §3.7.3.3).
            foreach (JObject mesh in glb.Array("meshes"))
            foreach (JObject p in (JArray)mesh["primitives"])
            {
                if (((int?)p["mode"] ?? 4) != 4) continue;
                var a = (JObject)p["attributes"];
                var prim = new Primitive { Material = (int?)p["material"] ?? -1 };
                prim.Positions = Vec3((glb.Floats((int)a["POSITION"])));
                int n = prim.Positions.Length;
                prim.Normals = a["NORMAL"] != null ? Vec3(glb.Floats((int)a["NORMAL"])) : new Vector3[n];
                prim.Uv = new Vector2[n];
                if (a["TEXCOORD_0"] != null)
                {
                    var uv = glb.Floats((int)a["TEXCOORD_0"]);
                    for (int i = 0; i < n; i++) prim.Uv[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
                }
                prim.Thickness = new float[n];
                if (a["COLOR_0"] != null)
                {
                    int accessor = (int)a["COLOR_0"];
                    var c = glb.Floats(accessor);
                    int width = c.Length / Math.Max(n, 1);
                    for (int i = 0; i < n; i++) prim.Thickness[i] = c[i * width + 1];
                }
                else for (int i = 0; i < n; i++) prim.Thickness[i] = 1;
                if (a["JOINTS_0"] != null && a["WEIGHTS_0"] != null)
                {
                    prim.Joints = glb.Ints((int)a["JOINTS_0"]);
                    prim.Weights = glb.Floats((int)a["WEIGHTS_0"]);
                }
                prim.Indices = p["indices"] != null ? glb.Ints((int)p["indices"]) : Sequence(n);
                Primitives.Add(prim);
            }
        }

        void FitFacePlane(bool normalize)
        {
            foreach (var prim in Primitives)
            {
                if (prim.Material != FaceMaterial || prim.Positions.Length == 0) continue;
                Vector3 min = prim.Positions[0], max = prim.Positions[0];
                foreach (var v in prim.Positions) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
                float size = Math.Max(max.x - min.x, max.y - min.y);
                if (size <= 0.0001f || !normalize && size <= FacePlaneSize * 1.02f) continue;
                var centre = (min + max) * 0.5f;
                float factor = FacePlaneSize / size;
                for (int i = 0; i < prim.Positions.Length; i++) prim.Positions[i] = centre + (prim.Positions[i] - centre) * factor;
            }
        }

        static Vector3[] Vec3(float[] f)
        {
            var r = new Vector3[f.Length / 3];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return r;
        }

        static int[] Sequence(int n)
        {
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = i;
            return r;
        }
    }

    // animations.glb: a skeleton without meshes and the 52 Don motions (26 and their _mirror
    // versions, in AnimIndex order). Each motion is sampled at 60 frames a second into model-space
    // joint poses, as YataiDON's load_model_animations bakes them; a part's skinning matrix for a
    // joint is then the pose times the part's inverse bind matrix.
    public sealed class DonMotions
    {
        public const float FrameRate = 60f;

        public string[] Names = Array.Empty<string>();
        public int[] FrameCounts = Array.Empty<int>();
        public Dictionary<string, int> Joints = new Dictionary<string, int>(StringComparer.Ordinal);
        // [motion][frame * joints + joint]
        public Matrix4x4[][] Poses = Array.Empty<Matrix4x4[]>();
        public int JointCount;

        public int Count => Names.Length;

        public static DonMotions Load(byte[] glbBytes)
        {
            var glb = Glb.Parse(glbBytes);
            var motions = new DonMotions();
            var skins = glb.Array("skins");
            var anims = glb.Array("animations");
            if (skins.Count == 0 || anims.Count == 0) return motions;
            var nodes = glb.Array("nodes");
            var jointNodes = new List<int>();
            foreach (var j in (JArray)skins[0]["joints"]) jointNodes.Add((int)j);
            int joints = jointNodes.Count;
            motions.JointCount = joints;
            var parentOf = new int[nodes.Count];
            for (int i = 0; i < parentOf.Length; i++) parentOf[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i]["children"] is JArray children)
                    foreach (var c in children) parentOf[(int)c] = i;
            var jointOfNode = new Dictionary<int, int>();
            for (int k = 0; k < joints; k++)
            {
                jointOfNode[jointNodes[k]] = k;
                motions.Joints[(string)nodes[jointNodes[k]]["name"] ?? ""] = k;
            }
            // The plain (non-joint) nodes above each joint, which raylib bakes into its pose, and
            // the joint each joint hangs from.
            var external = new Matrix4x4[joints];
            var parentJoint = new int[joints];
            for (int k = 0; k < joints; k++)
            {
                external[k] = Matrix4x4.identity;
                parentJoint[k] = -1;
                for (int n = parentOf[jointNodes[k]]; n >= 0; n = parentOf[n])
                {
                    if (jointOfNode.TryGetValue(n, out int pj)) { parentJoint[k] = pj; break; }
                    external[k] = Glb.NodeMatrix((JObject)nodes[n]) * external[k];
                }
            }
            var rest = new (Vector3 t, Quaternion r, Vector3 s)[joints];
            for (int k = 0; k < joints; k++) rest[k] = Glb.NodeTrs((JObject)nodes[jointNodes[k]]);
            var order = ParentsFirst(parentJoint);

            motions.Names = new string[anims.Count];
            motions.FrameCounts = new int[anims.Count];
            motions.Poses = new Matrix4x4[anims.Count][];
            for (int a = 0; a < anims.Count; a++)
            {
                var anim = (JObject)anims[a];
                var samplers = (JArray)anim["samplers"];
                var channels = new Sampler[joints, 3];
                float duration = 0;
                foreach (JObject ch in (JArray)anim["channels"])
                {
                    var target = (JObject)ch["target"];
                    if (target["node"] == null || !jointOfNode.TryGetValue((int)target["node"], out int k)) continue;
                    int path = (string)target["path"] switch { "translation" => 0, "rotation" => 1, "scale" => 2, _ => -1 };
                    if (path < 0) continue;
                    var s = (JObject)samplers[(int)ch["sampler"]];
                    var sampler = new Sampler
                    {
                        Times = glb.Floats((int)s["input"]),
                        Values = glb.Floats((int)s["output"]),
                        Width = path == 1 ? 4 : 3,
                        Step = (string)s["interpolation"] == "STEP",
                        Cubic = (string)s["interpolation"] == "CUBICSPLINE",
                    };
                    channels[k, path] = sampler;
                    if (sampler.Times.Length > 0) duration = Math.Max(duration, sampler.Times[^1]);
                }
                int frames = (int)(duration * FrameRate) + 1;
                motions.Names[a] = (string)anim["name"] ?? "";
                motions.FrameCounts[a] = frames;
                var poses = motions.Poses[a] = new Matrix4x4[frames * joints];
                var t = new Vector3[joints];
                var r = new Quaternion[joints];
                var sc = new Vector3[joints];
                for (int f = 0; f < frames; f++)
                {
                    float time = f / FrameRate;
                    for (int k = 0; k < joints; k++)
                    {
                        var (tr, ro, sca) = rest[k];
                        if (channels[k, 0] is Sampler st) tr = st.Vector(time, tr);
                        if (channels[k, 1] is Sampler sr) ro = sr.Rotation(time, ro);
                        if (channels[k, 2] is Sampler ss) sca = ss.Vector(time, sca);
                        var local = external[k] * Matrix4x4.TRS(tr, ro, sca);
                        (t[k], r[k], sc[k]) = Decompose(local);
                    }
                    // raylib BuildPoseFromParentJoints: compose each joint onto its parent's pose.
                    foreach (int k in order)
                    {
                        int p = parentJoint[k];
                        if (p < 0) continue;
                        r[k] = r[p] * r[k];
                        t[k] = r[p] * Vector3.Scale(t[k], sc[p]) + t[p];
                        sc[k] = Vector3.Scale(sc[k], sc[p]);
                    }
                    for (int k = 0; k < joints; k++) poses[f * joints + k] = Matrix4x4.TRS(t[k], r[k], sc[k]);
                }
            }
            return motions;
        }

        static List<int> ParentsFirst(int[] parent)
        {
            var order = new List<int>();
            var done = new bool[parent.Length];
            void Visit(int k)
            {
                if (done[k]) return;
                if (parent[k] >= 0) Visit(parent[k]);
                done[k] = true; order.Add(k);
            }
            for (int k = 0; k < parent.Length; k++) Visit(k);
            return order;
        }

        static (Vector3, Quaternion, Vector3) Decompose(Matrix4x4 m)
        {
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            var s = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            var rot = Quaternion.identity;
            if (s.x > 1e-8f && s.y > 1e-8f && s.z > 1e-8f)
                rot = Quaternion.LookRotation(c2 / s.z, c1 / s.y);
            return ((Vector3)m.GetColumn(3), rot, s);
        }

        sealed class Sampler
        {
            public float[] Times, Values;
            public int Width;
            public bool Step, Cubic;

            // The keyframe before time and how far toward the next.
            (int i, float u) Find(float time)
            {
                int n = Times.Length;
                if (n == 0 || time <= Times[0]) return (0, 0);
                if (time >= Times[n - 1]) return (n - 1, 0);
                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (Times[mid] <= time) lo = mid; else hi = mid;
                }
                float span = Times[hi] - Times[lo];
                return (lo, span > 0 && !Step ? (time - Times[lo]) / span : 0);
            }

            float Value(int key, int c) => Cubic ? Values[(key * 3 + 1) * Width + c] : Values[key * Width + c];

            public Vector3 Vector(float time, Vector3 fallback)
            {
                if (Times.Length == 0) return fallback;
                var (i, u) = Find(time);
                var a = new Vector3(Value(i, 0), Value(i, 1), Value(i, 2));
                if (u <= 0 || i + 1 >= Times.Length) return a;
                var b = new Vector3(Value(i + 1, 0), Value(i + 1, 1), Value(i + 1, 2));
                return Vector3.LerpUnclamped(a, b, u);
            }

            public Quaternion Rotation(float time, Quaternion fallback)
            {
                if (Times.Length == 0) return fallback;
                var (i, u) = Find(time);
                var a = new Quaternion(Value(i, 0), Value(i, 1), Value(i, 2), Value(i, 3));
                if (u <= 0 || i + 1 >= Times.Length) return Normalize(a);
                var b = new Quaternion(Value(i + 1, 0), Value(i + 1, 1), Value(i + 1, 2), Value(i + 1, 3));
                return Quaternion.SlerpUnclamped(Normalize(a), Normalize(b), u);
            }

            static Quaternion Normalize(Quaternion q)
            {
                float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                return m > 1e-8f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
            }
        }
    }

    // YataiDON's face timelines (Graphics/global/animation.json "texture_change" entries, the ones
    // its FACE_ANIM_IDS name), copied to Resources/Chara/face_animations.json: which face of the
    // sheet shows over the first `duration` real-time milliseconds after a motion starts.
    public sealed class FaceTimeline
    {
        public double Duration;
        public bool Loop;
        public (double start, double end, int face)[] Frames = Array.Empty<(double, double, int)>();

        public int FirstFace => Frames.Length > 0 ? Frames[0].face : 0;

        public static Dictionary<int, FaceTimeline> Parse(string json)
        {
            var result = new Dictionary<int, FaceTimeline>();
            foreach (JObject entry in JArray.Parse(json))
            {
                if ((string)entry["type"] != "texture_change" || entry["id"] == null) continue;
                var frames = new List<(double, double, int)>();
                foreach (JArray f in (JArray)entry["textures"]) frames.Add(((double)f[0], (double)f[1], (int)f[2]));
                result[(int)entry["id"]] = new FaceTimeline
                {
                    Duration = (double?)entry["duration"] ?? 0,
                    Loop = (bool?)entry["loop"] ?? false,
                    Frames = frames.ToArray(),
                };
            }
            return result;
        }
    }

    // A face timeline playing (YataiDON's TextureChangeAnimation with BaseAnimation's loop).
    sealed class FacePlayer
    {
        readonly FaceTimeline timeline;
        double startMs;
        bool finished;
        public int Face { get; private set; }

        public FacePlayer(FaceTimeline timeline, double now)
        {
            this.timeline = timeline;
            startMs = now;
            Face = timeline.FirstFace;
        }

        public void Update(double now)
        {
            if (timeline.Loop && finished) { startMs = now; finished = false; }
            if (finished) return;
            double t = now - startMs;
            if (t <= timeline.Duration)
            {
                foreach (var (start, end, face) in timeline.Frames)
                    if (start < t && t <= end) Face = face;
            }
            else finished = true;
        }
    }

    // Loads and keeps what every Don shares: parts by path, the motions, the face sheet and
    // timelines, and puchichara pictures. The game carries the plain Don under Resources/Chara
    // (costume 0, the motions, the face sheet and timelines, from YataiDON's PyTaikoGreen skin);
    // every other costume, head, body and puchichara is a file DonDownloads fetched from the
    // public bucket (3dchara/) and named here with UseFile.
    public static class DonLibrary
    {
        public const string Root = "Chara/";
        // Parts by path: the ones a Don wears, and the last few nobody wears (so going back and
        // forth between costumes does not reload them). The rest are freed.
        sealed class Entry { public DonPart Part; public int Users; public long LastUsed; }
        static readonly Dictionary<string, Entry> parts = new Dictionary<string, Entry>();
        public const int IdlePartsKept = 8;
        static long clock;
        static DonMotions motions;
        // Downloaded files by "kind/id" (cos, head, body, puchi): what Part and Puchi read first.
        static readonly Dictionary<string, string> files = new Dictionary<string, string>();

        public static void UseFile(string kind, int index, string path) => files[kind + "/" + index] = path;

        public static bool HasFile(string kind, int index) => files.ContainsKey(kind + "/" + index);
        static Dictionary<int, FaceTimeline> faces;
        static Texture2D faceSheet;
        static readonly Dictionary<int, Texture2D> puchi = new Dictionary<int, Texture2D>();

        // The Editor here enters Play mode without reloading the domain (EditorSettings: Enter Play
        // Mode Options), so these statics outlive a Play session, while every texture made in it is
        // destroyed when it stops: a cached part would come back with its images gone and its Don
        // drawn white. Each Play session starts from nothing (as a player does).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            parts.Clear();
            puchi.Clear();
            files.Clear();
            faceSheet = null;
        }

        // A texture Unity destroyed (== null) that was there (not a reference that was null from the start).
        static bool Destroyed(UnityEngine.Object o) => !ReferenceEquals(o, null) && o == null;

        // "cos", "head" or "body"; null when that model is not in the build. Peeks: Acquire it to keep it.
        public static DonPart Part(string kind, int index, bool normalizeFace = false) => Find(kind, index, normalizeFace)?.Part;

        // A part a Don wears until it calls Release.
        public static DonPart Acquire(string kind, int index, bool normalizeFace = false)
        {
            var entry = Find(kind, index, normalizeFace);
            if (entry?.Part == null) return null;
            entry.Users++;
            return entry.Part;
        }

        public static void Release(DonPart part)
        {
            foreach (var entry in parts.Values)
                if (entry.Part == part && entry.Users > 0)
                {
                    entry.Users--;
                    entry.LastUsed = ++clock;
                    break;
                }
            Trim();
        }

        static Entry Find(string kind, int index, bool normalizeFace)
        {
            files.TryGetValue(kind + "/" + index, out string file);
            string path = file ?? Root + kind + "/" + index;
            string key = path + (normalizeFace ? "#face" : "");
            // A part whose images were destroyed under it (a Play session ended) is read again.
            if (parts.TryGetValue(key, out var cached) && System.Array.Exists(cached.Part.Images, Destroyed)) parts.Remove(key);
            if (!parts.TryGetValue(key, out var entry))
            {
                byte[] bytes = null;
                if (file != null)
                {
                    try { bytes = System.IO.File.ReadAllBytes(file); }
                    catch (System.Exception error) { Log.Warn("Don", "Could not read " + file + ": " + error.Message); }
                }
                else
                {
                    var asset = Resources.Load<GlbAsset>(path);
                    if (asset != null) { bytes = asset.bytes; Resources.UnloadAsset(asset); }
                }
                if (bytes == null) return null;
                try { entry = new Entry { Part = DonPart.Load(bytes, normalizeFace) }; }
                catch (System.Exception error)
                {
                    Log.Warn("Don", "Could not read the model " + path + ": " + error.Message);
                    return null;
                }
                parts[key] = entry;
            }
            entry.LastUsed = ++clock;
            Trim();
            return entry;
        }

        static void Trim()
        {
            var idle = new List<KeyValuePair<string, Entry>>();
            foreach (var pair in parts) if (pair.Value.Users == 0) idle.Add(pair);
            if (idle.Count <= IdlePartsKept) return;
            idle.Sort((a, b) => a.Value.LastUsed.CompareTo(b.Value.LastUsed));
            for (int i = 0; i < idle.Count - IdlePartsKept; i++)
            {
                foreach (var image in idle[i].Value.Part.Images)
                    if (image != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(image); else UnityEngine.Object.DestroyImmediate(image); }
                parts.Remove(idle[i].Key);
            }
        }

        public static int CachedParts => parts.Count;

        public static DonMotions Motions
        {
            get
            {
                if (motions != null) return motions;
                var asset = Resources.Load<GlbAsset>(Root + "animations");
                motions = asset != null && asset.bytes != null ? DonMotions.Load(asset.bytes) : new DonMotions();
                if (asset != null) Resources.UnloadAsset(asset);
                return motions;
            }
        }

        public static Dictionary<int, FaceTimeline> FaceTimelines
        {
            get
            {
                if (faces != null) return faces;
                var text = Resources.Load<TextAsset>(Root + "face_animations");
                faces = text != null ? FaceTimeline.Parse(text.text) : new Dictionary<int, FaceTimeline>();
                return faces;
            }
        }

        // The face sheet: square faces stacked top to bottom (YataiDON takes the first face_*.png).
        public static Texture2D FaceSheet => faceSheet != null ? faceSheet : faceSheet = Resources.Load<Texture2D>(Root + "face/face_000000");

        public static int FaceCount
        {
            get
            {
                var sheet = FaceSheet;
                return sheet != null && sheet.width > 0 ? sheet.height / sheet.width : 0;
            }
        }

        // A puchichara's two-frame picture (frames stacked top to bottom); null for 0 or one that
        // has not been downloaded.
        public static Texture2D Puchi(int index)
        {
            if (index <= 0 || !files.TryGetValue("puchi/" + index, out string file)) return null;
            if (puchi.TryGetValue(index, out var texture) && texture != null && texture.name == file) return texture;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    { name = file, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                texture.LoadImage(System.IO.File.ReadAllBytes(file), true);
            }
            catch (System.Exception error)
            {
                Log.Warn("Don", "Could not read " + file + ": " + error.Message);
                texture = null;
            }
            puchi[index] = texture;
            return texture;
        }
    }
}
