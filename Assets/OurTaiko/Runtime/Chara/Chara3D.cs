using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OurTaiko.Chara
{
    // The motions of animations.glb, in its order (YataiDON's AnimIndex). The second half are the
    // same motions for a Don facing the other way; Chara3D picks them itself when mirrored.
    public enum DonAnim
    {
        BalloonFailure = 0, BalloonLoop = 1, BalloonNoBeat = 2, BalloonSuccess = 3, Bind = 4, Combo = 5,
        EntryIn = 6, EntryLoop = 7, FukkatuLoop = 8, FukkatuStart = 9, FullCombo = 10, FullGage = 11,
        GeneralJump = 12, GeneralLoop = 13, Miss = 14, Miss6 = 15, MissNormal = 16, NormDown = 17,
        NormLoop = 18, NormUp = 19, Normal = 20, Sabi = 21, SabiStart = 22, SelectLoop = 23,
        SelectPanelDown = 24, SelectPanelUp = 25,
        MirrorOffset = 26,
        Miss6Mirror = 40, MissMirror = 41,
    }

    // How a Don looks: a whole costume, or a head and a body; the three body colours, the face of
    // the sheet it starts with, the puchichara beside it (0: none) and its makeup (0: none).
    // YataiDON's PlayerData chara_* fields.
    [Serializable]
    public struct DonLook
    {
        public bool costume;
        public int cos, head, body, face, puchi, paint;
        public Color32 color1, color2, color3;

        public static readonly Color32 Rim = new Color32(249, 240, 225, 255);

        // YataiDON chara_default_color_1/2: 1P is the blue-rimmed red Don, 2P the reverse.
        public static DonLook Default(int seat = 0) => new DonLook
        {
            costume = true,
            color1 = seat == 0 ? new Color32(104, 191, 192, 255) : new Color32(249, 71, 40, 255),
            color2 = seat == 0 ? new Color32(249, 71, 40, 255) : new Color32(104, 191, 192, 255),
            color3 = Rim,
        };
    }

    // A 3D Don (YataiDON's Chara3D): its parts from Resources/Chara, the motion it plays (stepped
    // by the song's tempo), its face, colours and puchichara. It draws itself into a render target
    // in glTF space with YataiDON's camera; DonView puts that target on a canvas with the black
    // outline and tint pass. Plain object: DonView owns one and disposes it.
    public sealed class Chara3D : IDisposable
    {
        // YataiDON Chara3DConfig defaults and the PyTaikoGreen skin's chara_3d (which the Nijiiro skin
        // inherits): the Don's size in 720-line pixels and its turn in degrees. Most places draw with
        // the defaults; play and the AI window use the skin's 23° (useSkinPose).
        public const float Scale720 = 650f, RotX = 181.25f, RotY = 27.5f, SkinRotY = 23f, RotZ = 0f;

        // Which face timeline each motion starts (YataiDON FACE_ANIM_IDS), and the revive face.
        static readonly int[] FaceAnimIds =
        {
            13, 14, 15, 16, 65, 17, 22, 19, 30, 29, 23, 24, 25, 26, 40, 40, 42, 43, 44, 45, 46, 58, 59, 62, 60, 18,
            13, 14, 15, 16, 64, 17, 22, 21, 36, 35, 23, 24, 25, 26, 40, 40, 42, 43, 44, 45, 46, 58, 59, 62, 61, 20,
        };
        const int FukkatuFace = 8;
        // YataiDON PAINT_PLANE_SCALE: makeup is drawn on a face plate a third larger.
        public const float PaintPlaneScale = 4f / 3f;
        const int Mirror = (int)DonAnim.MirrorOffset;

        sealed class Part
        {
            public DonPart Data;
            public Mesh[] Meshes;
            public Vector3[][] Positions, Normals;
            // The bind positions skinning starts from: the part's own, but the face plates' are
            // this Don's own copy while it wears makeup (the part data is shared).
            public Vector3[][] Bind;
            public Material[] Materials;
            public Texture2D[] Recolored;
            public int[] JointMap;
            public Matrix4x4[] Skin;
        }

        readonly List<Part> parts = new List<Part>();
        readonly DonMotions motions;
        readonly Dictionary<int, FaceTimeline> faceTimelines;
        static Shader modelShader;

        public bool IsEmpty => parts.Count == 0;

        int animFrame, appliedAnim = -1, appliedFrame = -1;
        int animIndex = (int)DonAnim.Normal, prevAnimIndex = (int)DonAnim.Normal;
        bool looping = true, frozen;
        double lastFrameMs;
        FacePlayer facePlayer;
        int currentFace = -1;
        Texture2D faceSheet;
        int faceCount;

        public float Bpm { get; set; } = 100;
        public bool Mirrored { get; private set; }
        public float RotationX { get; private set; } = RotX;
        public float RotationY { get; private set; } = RotY;
        public float RotationZ { get; private set; } = RotZ;
        public Color TintColor { get; private set; } = Color.black;
        public float TintAmount { get; private set; }
        public int PuchiIndex { get; private set; }
        public Texture2D PuchiTexture { get; private set; }
        // The puchichara's animation when it is an .otanim (PuchiTexture is then its texture).
        public Otanim PuchiAnim { get; private set; }
        public double PuchiMs { get; private set; }
        public int PaintIndex { get; private set; }
        // The sheet the face plates show: the plain one, or the makeup's.
        public Texture2D FaceSheet => faceSheet;
        // Something the drawn picture shows changed since the last Render.
        public bool Dirty { get; set; } = true;
        // The part of the area the last Render drew in, black line included (units, y down from the
        // area's top left; it may reach past the area). Empty when there was nothing to draw.
        public Rect Covered { get; private set; }
        public DonAnim Anim => (DonAnim)animIndex;
        public int Face => currentFace;

        public static double NowMs => Time.realtimeSinceStartupAsDouble * 1000;

        // A costume (look.costume) or a head and a body, recoloured, with its face and puchichara.
        public Chara3D(DonLook look, bool mirror = false, bool useSkinPose = false)
        {
            motions = DonLibrary.Motions;
            faceTimelines = DonLibrary.FaceTimelines;
            if (modelShader == null) modelShader = Resources.Load<Shader>(DonLibrary.Root + "DonModel");
            Mirrored = mirror;
            if (useSkinPose) RotationY = SkinRotY;
            faceSheet = DonLibrary.FaceSheet;
            faceCount = SheetFaces(faceSheet);
            if (look.costume)
            {
                AddPart(DonLibrary.Acquire("cos", look.cos) ?? DonLibrary.Acquire("cos", 0));
            }
            else
            {
                AddPart(DonLibrary.Acquire("body", look.body) ?? DonLibrary.Acquire("body", 0));
                AddPart(DonLibrary.Acquire("head", look.head, true) ?? DonLibrary.Acquire("head", 0, true));
            }
            // YataiDON's order: the constructor's first face and motion, then make_chara_from_player_data's
            // colours, face and puchichara (the motion's face timeline takes over at the next Update).
            ApplyFace(0);
            SetAnim(DonAnim.Normal);
            SetDonColors(look.color1, look.color2, look.color3);
            ApplyFace(look.face);
            SetPuchi(look.puchi);
            SetPaint(look.paint);
        }

        void AddPart(DonPart data)
        {
            if (data == null) return;
            var part = new Part
            {
                Data = data,
                Meshes = new Mesh[data.Primitives.Count],
                Positions = new Vector3[data.Primitives.Count][],
                Normals = new Vector3[data.Primitives.Count][],
                Bind = new Vector3[data.Primitives.Count][],
                Materials = new Material[data.Materials.Count],
                Recolored = new Texture2D[data.Materials.Count],
                JointMap = new int[data.JointNames.Length],
                Skin = new Matrix4x4[data.JointNames.Length],
            };
            for (int j = 0; j < part.JointMap.Length; j++)
            {
                part.JointMap[j] = motions.Joints.TryGetValue(data.JointNames[j], out int k) ? k : -1;
                part.Skin[j] = Matrix4x4.identity;
            }
            for (int m = 0; m < data.Materials.Count; m++)
            {
                var info = data.Materials[m];
                var material = new Material(modelShader) { name = info.Name, hideFlags = HideFlags.DontSave };
                if (info.Face) material.mainTexture = faceSheet;
                else if (info.Image >= 0 && info.Image < data.Images.Length) material.mainTexture = data.Images[info.Image];
                material.SetColor("_Color", info.Color);
                // glTF space (right-handed, as raylib draws it) through Unity's matrices turns the
                // winding round: raylib's back-face culling is Front here and its Front (the outline
                // hull) is Back.
                material.SetFloat("_Cull", (float)(info.TwoSided ? CullMode.Off : CullMode.Front));
                material.SetFloat("_OutlineCull", (float)CullMode.Back);
                material.SetFloat("_AlphaClip", info.Face ? 0 : 1);
                material.SetVector("_UvRect", new Vector4(1, 1, 0, 0));
                part.Materials[m] = material;
            }
            for (int p = 0; p < data.Primitives.Count; p++)
            {
                var prim = data.Primitives[p];
                var mesh = new Mesh { name = "Don part", hideFlags = HideFlags.DontSave };
                if (prim.Positions.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.MarkDynamic();
                part.Bind[p] = prim.Positions;
                part.Positions[p] = (Vector3[])prim.Positions.Clone();
                part.Normals[p] = (Vector3[])prim.Normals.Clone();
                mesh.vertices = part.Positions[p];
                mesh.normals = part.Normals[p];
                mesh.uv = prim.Uv;
                var colors = new Color[prim.Thickness.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(1, prim.Thickness[i], 1, 1);
                mesh.colors = colors;
                mesh.SetTriangles(prim.Indices, 0, false);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10);
                part.Meshes[p] = mesh;
            }
            parts.Add(part);
        }

        public void Dispose()
        {
            foreach (var part in parts)
            {
                foreach (var mesh in part.Meshes) Destroy(mesh);
                foreach (var material in part.Materials) Destroy(material);
                foreach (var texture in part.Recolored) Destroy(texture);
                DonLibrary.Release(part.Data);
            }
            parts.Clear();
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // ---- look ----

        static int SheetFaces(Texture2D sheet) => sheet != null && sheet.width > 0 ? sheet.height / sheet.width : 0;

        public void ApplyFace(int face)
        {
            int count = faceCount;
            if (face < 0 || face >= count) return;
            var rect = new Vector4(1, 1f / count, 0, 1f - (face + 1f) / count);
            foreach (var part in parts)
                if (part.Data.FaceMaterial >= 0) part.Materials[part.Data.FaceMaterial].SetVector("_UvRect", rect);
            currentFace = face;
            Dirty = true;
        }

        // YataiDON recolor_texture: on the recolourable textures, reddish texels take the body
        // colour, greenish the face colour and bluish the rim; greys and near-blacks stay.
        public void SetDonColors(Color32 body, Color32 face, Color32 rim)
        {
            foreach (var part in parts)
            {
                for (int m = 0; m < part.Materials.Length; m++)
                {
                    var info = part.Data.Materials[m];
                    if (!info.Recolor || info.Image < 0 || info.Image >= part.Data.Images.Length) continue;
                    var source = part.Data.Images[info.Image];
                    if (source == null) continue;
                    var pixels = source.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var c = pixels[i];
                        int strongest = Math.Max(c.r, Math.Max(c.g, c.b)), weakest = Math.Min(c.r, Math.Min(c.g, c.b));
                        if (strongest <= 0.05f * 255 || strongest - weakest <= 0.08f * 255) continue;
                        Color32 o = c.b > c.r && c.b >= c.g ? rim : c.g > c.r && c.g > c.b ? face : body;
                        pixels[i] = new Color32(o.r, o.g, o.b, c.a);
                    }
                    var texture = part.Recolored[m];
                    if (texture == null || texture.width != source.width || texture.height != source.height)
                    {
                        Destroy(texture);
                        texture = part.Recolored[m] = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
                        {
                            name = "Don recoloured", hideFlags = HideFlags.DontSave,
                            filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat,
                        };
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply(true, false);
                    part.Materials[m].mainTexture = texture;
                }
            }
            Dirty = true;
        }

        public void SetPuchi(int index)
        {
            if (index == PuchiIndex && (index <= 0 || PuchiTexture != null)) return;
            PuchiIndex = index;
            PuchiTexture = DonLibrary.Puchi(index);
            PuchiAnim = DonLibrary.PuchiAnim(index);
        }

        // YataiDON Chara3D::set_paint: the makeup's face sheet (3dchara/paint/<index>.png, downloaded)
        // in place of the plain one, the face plates a third larger while it is worn; 0 is the
        // plain face again. A makeup that is not here changes nothing.
        public void SetPaint(int index)
        {
            if (index < 0) index = 0;
            if (index == PaintIndex) return;
            var sheet = index > 0 ? DonLibrary.Paint(index) : DonLibrary.FaceSheet;
            if (sheet == null) return;
            if ((index > 0) != (PaintIndex > 0)) ScaleFacePlates(index > 0 ? PaintPlaneScale : 1f);
            PaintIndex = index;
            faceSheet = sheet;
            faceCount = SheetFaces(sheet);
            foreach (var part in parts)
                if (part.Data.FaceMaterial >= 0) part.Materials[part.Data.FaceMaterial].mainTexture = sheet;
            int face = Math.Max(currentFace, 0);
            currentFace = -1;
            ApplyFace(face);
            Dirty = true;
        }

        // YataiDON scale_face_planes: each face plate `factor` times its own size about the centre of
        // its box (1: as the part has it); the next pose skins from there.
        void ScaleFacePlates(float factor)
        {
            foreach (var part in parts)
            {
                var data = part.Data;
                for (int p = 0; p < data.Primitives.Count; p++)
                {
                    var prim = data.Primitives[p];
                    if (prim.Material != data.FaceMaterial || prim.Positions.Length == 0) continue;
                    var bind = prim.Positions;
                    if (factor != 1f)
                    {
                        Vector3 min = bind[0], max = bind[0];
                        foreach (var v in bind) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
                        var centre = (min + max) * 0.5f;
                        bind = new Vector3[prim.Positions.Length];
                        for (int i = 0; i < bind.Length; i++) bind[i] = centre + (prim.Positions[i] - centre) * factor;
                    }
                    part.Bind[p] = bind;
                    if (prim.Joints == null || part.Skin.Length == 0)
                    {
                        // not skinned: the plate stays where it is drawn
                        part.Positions[p] = (Vector3[])bind.Clone();
                        part.Meshes[p].vertices = part.Positions[p];
                    }
                }
            }
            appliedAnim = -1;
            appliedFrame = -1;
            ApplyPose();
            Dirty = true;
        }

        public void SetTint(Color color, float amount)
        {
            if (TintColor == color && TintAmount == amount) return;
            TintColor = color; TintAmount = amount;
        }

        public void SetRotation(float x, float y, float z)
        {
            if (RotationX == x && RotationY == y && RotationZ == z) return;
            RotationX = x; RotationY = y; RotationZ = z;
            Dirty = true;
        }

        // Back to YataiDON's default size and turn, facing the given way.
        public void UseDefaultPose(bool mirror)
        {
            RotationX = RotX; RotationY = RotY; RotationZ = RotZ;
            Mirrored = mirror;
            SetAnim((DonAnim)animIndex);
            Dirty = true;
        }

        // Turns the Don round, keeping the running motion's place.
        public void SetMirror(bool value)
        {
            if (Mirrored == value) return;
            Mirrored = value;
            int frame = animFrame;
            double frameMs = lastFrameMs;
            SetAnim((DonAnim)animIndex);
            animFrame = frame;
            lastFrameMs = frameMs;
            Dirty = true;
        }

        // ---- motion ----

        // The motion a (possibly mirrored) index is. In the mirrored half miss6 comes before miss
        // (the glb's names sort that way), so 40 is DON_MISS6 and 41 DON_MISS; YataiDON's base_anim
        // (index % 26) has them the wrong way round, which turned a mirrored Don's miss into miss6
        // when it was turned round or went back to it.
        static int BaseAnim(int index) => index == (int)DonAnim.Miss6Mirror ? (int)DonAnim.Miss6
            : index == (int)DonAnim.MissMirror ? (int)DonAnim.Miss : index % Mirror;
        public int AnimCount => motions.Count;
        public string AnimName(int index) => index >= 0 && index < motions.Count ? motions.Names[index] : "";

        // YataiDON Chara3D::set_anim. Loops (and the motions that hold) become what a one-shot
        // returns to when it ends; ENTRY_IN and FUKKATU_START queue their loop first.
        public void SetAnim(DonAnim anim)
        {
            int idx = (int)anim;
            int b = BaseAnim(idx);
            if (b == (int)DonAnim.EntryIn) SetAnim(DonAnim.EntryLoop);
            else if (b == (int)DonAnim.FukkatuStart) SetAnim(DonAnim.FukkatuLoop);
            int i = b + (Mirrored ? Mirror : 0);
            if (Mirrored && (b == (int)DonAnim.Miss || b == (int)DonAnim.Miss6))
                i = (int)DonAnim.Miss6Mirror + (int)DonAnim.MissMirror - i;
            if (i >= 0 && i < motions.Count)
            {
                if (b == (int)DonAnim.Normal || b == (int)DonAnim.Sabi || b == (int)DonAnim.BalloonNoBeat ||
                    b == (int)DonAnim.Miss6 || AnimName(i).Contains("loop"))
                    looping = true;
                else
                {
                    if (looping) prevAnimIndex = animIndex; // not while chaining one-shots, or one would return to itself
                    looping = false;
                }
                animIndex = i;
                animFrame = 0;
                lastFrameMs = 0;
                Dirty = true;
                if (frozen) ApplyPose();
            }
            if (b == (int)DonAnim.FukkatuLoop || b == (int)DonAnim.FukkatuStart)
            {
                // the revive sequence holds one face instead of running a face animation
                facePlayer = null;
                ApplyFace(FukkatuFace);
            }
            else if (i >= 0 && i < FaceAnimIds.Length && faceTimelines.TryGetValue(FaceAnimIds[i], out var timeline))
            {
                facePlayer = new FacePlayer(timeline, NowMs);
                ApplyFace(facePlayer.Face);
            }
        }

        public void SetFrozen(bool value)
        {
            if (frozen == value) return;
            frozen = value;
            if (frozen) ApplyPose(); else lastFrameMs = 0;
        }

        // Holds the running motion at a point of its loop (0..1, wrapped).
        public void SetAnimProgress(float progress)
        {
            if (animIndex < 0 || animIndex >= motions.Count) return;
            int loopFrames = motions.FrameCounts[animIndex] - 1;
            if (loopFrames <= 0) return;
            float cycle = progress - Mathf.Floor(progress);
            animFrame = Math.Min(loopFrames - 1, (int)(cycle * loopFrames));
            ApplyPose();
        }

        // YataiDON Chara3D::update: a motion's loop lasts one beat (DON_NORMAL three, DON_SABI six,
        // the balloon loop half), stepped a keyframe at a time; a one-shot then goes back.
        public void Update(double currentMs)
        {
            PuchiMs = currentMs;
            if (!frozen && animIndex >= 0 && animIndex < motions.Count)
            {
                int kf = motions.FrameCounts[animIndex];
                if (Bpm > 0 && kf > 0)
                {
                    double msPerBeat = 60000.0 / Bpm;
                    int b = BaseAnim(animIndex);
                    if (b == (int)DonAnim.Normal) msPerBeat *= 3;
                    else if (b == (int)DonAnim.Sabi) msPerBeat *= 6;
                    if (b == (int)DonAnim.BalloonLoop) msPerBeat /= 2;
                    double msPerFrame = msPerBeat / kf;
                    if (currentMs - lastFrameMs >= msPerFrame)
                    {
                        int loopFrames = kf - 1;
                        lastFrameMs = currentMs;
                        if (loopFrames <= 0)
                        {
                            if (!looping) { SetAnim((DonAnim)prevAnimIndex); looping = true; }
                        }
                        else
                        {
                            animFrame = (animFrame + 1) % loopFrames;
                            ApplyPose();
                            if (!looping && animFrame == loopFrames - 1) { SetAnim((DonAnim)prevAnimIndex); looping = true; }
                        }
                    }
                }
            }
            if (facePlayer != null)
            {
                facePlayer.Update(NowMs);
                if (facePlayer.Face != currentFace) ApplyFace(facePlayer.Face);
            }
        }

        void ApplyPose()
        {
            if (appliedAnim == animIndex && appliedFrame == animFrame) return;
            if (animIndex < 0 || animIndex >= motions.Count) return;
            var poses = motions.Poses[animIndex];
            int joints = motions.JointCount;
            int frame = Math.Min(animFrame, motions.FrameCounts[animIndex] - 1);
            foreach (var part in parts)
            {
                for (int j = 0; j < part.Skin.Length; j++)
                {
                    int k = part.JointMap[j];
                    // bones the glb does not animate stay at the part's bind pose
                    part.Skin[j] = k >= 0 ? poses[frame * joints + k] * part.Data.InverseBind[j] : Matrix4x4.identity;
                }
                SkinPart(part);
            }
            appliedAnim = animIndex;
            appliedFrame = animFrame;
            Dirty = true;
        }

        static void SkinPart(Part part)
        {
            var data = part.Data;
            for (int p = 0; p < data.Primitives.Count; p++)
            {
                var prim = data.Primitives[p];
                if (prim.Joints == null || part.Skin.Length == 0) continue;
                var pos = part.Positions[p];
                var nrm = part.Normals[p];
                for (int v = 0; v < pos.Length; v++)
                {
                    Matrix4x4 m = default;
                    float total = 0;
                    for (int w = 0; w < 4; w++)
                    {
                        float weight = prim.Weights[v * 4 + w];
                        if (weight <= 0) continue;
                        int j = prim.Joints[v * 4 + w];
                        if (j < 0 || j >= part.Skin.Length) continue;
                        Add(ref m, part.Skin[j], weight);
                        total += weight;
                    }
                    if (total <= 0) { pos[v] = part.Bind[p][v]; nrm[v] = prim.Normals[v]; continue; }
                    pos[v] = m.MultiplyPoint3x4(part.Bind[p][v]);
                    nrm[v] = m.MultiplyVector(prim.Normals[v]);
                }
                part.Meshes[p].vertices = pos;
                part.Meshes[p].normals = nrm;
            }
        }

        static void Add(ref Matrix4x4 m, in Matrix4x4 s, float w)
        {
            m.m00 += s.m00 * w; m.m01 += s.m01 * w; m.m02 += s.m02 * w; m.m03 += s.m03 * w;
            m.m10 += s.m10 * w; m.m11 += s.m11 * w; m.m12 += s.m12 * w; m.m13 += s.m13 * w;
            m.m20 += s.m20 * w; m.m21 += s.m21 * w; m.m22 += s.m22 * w; m.m23 += s.m23 * w;
        }

        // ---- drawing ----

        // YataiDON rotation_xyz (angles in degrees, negated as it does).
        static Matrix4x4 RotationXyz(float ax, float ay, float az)
        {
            float cx = Mathf.Cos(-ax), sx = Mathf.Sin(-ax);
            float cy = Mathf.Cos(-ay), sy = Mathf.Sin(-ay);
            float cz = Mathf.Cos(-az), sz = Mathf.Sin(-az);
            // raylib layout: m0 m4 m8 is the first row, m1 m5 m9 the second, m2 m6 m10 the third.
            var r = Matrix4x4.identity;
            r[0, 0] = cz * cy;                  r[0, 1] = sz * cy;                  r[0, 2] = -sy;
            r[1, 0] = cz * sy * sx - sz * cx;   r[1, 1] = sz * sy * sx + cz * cx;   r[1, 2] = cy * sx;
            r[2, 0] = cz * sy * cx + sz * sx;   r[2, 1] = sz * sy * cx - cz * sx;   r[2, 2] = cy * cx;
            return r;
        }

        // The Don's transform onto a 2D area (y down) where it stands at (x, y), `size` units a metre.
        public Matrix4x4 ModelMatrix(float x, float y, float size)
        {
            float yAngle = Mirrored ? -RotationY : RotationY;
            var rot = RotationXyz(RotationX * Mathf.Deg2Rad, yAngle * Mathf.Deg2Rad, RotationZ * Mathf.Deg2Rad);
            return Matrix4x4.Translate(new Vector3(x, y, 400f)) * Matrix4x4.Scale(Vector3.one * size) * rot;
        }

        // Draws into `target` the area width × height (units, y down from its top left) with the
        // Don standing at (x, y): YataiDON's orthographic camera from z = -100 looking at +z, up -y.
        // outlinePx is the black line's width in target pixels (YataiDON: 2.5 per 720 lines).
        public void Render(CommandBuffer cmd, RenderTexture target, float width, float height, float x, float y, float size, float outlinePx)
        {
            // The whole target is cleared and drawn again, and only its picture is read afterwards:
            // nothing of the last one is loaded, the samples are resolved without being kept, and
            // the depth is thrown away. Loading and storing the multisampled colour and depth of a
            // target this size on every redraw is memory traffic a phone's GPU feels.
            var color = target.antiAliasing > 1 ? RenderBufferStoreAction.Resolve : RenderBufferStoreAction.Store;
            cmd.SetRenderTarget(target, RenderBufferLoadAction.DontCare, color, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
            cmd.ClearRenderTarget(true, true, Color.clear);
            Covered = default;
            if (parts.Count == 0) { Dirty = false; return; }
            float cx = width * 0.5f, cy = height * 0.5f;
            var view = Matrix4x4.identity;
            view[0, 0] = 1; view[0, 3] = -cx;
            view[1, 1] = -1; view[1, 3] = cy;
            view[2, 2] = -1; view[2, 3] = -100f;
            var projection = Matrix4x4.Ortho(-cx, cx, -cy, cy, 0.01f, 1000f);
            cmd.SetViewProjectionMatrices(view, projection);
            cmd.SetGlobalVector(OutlineId, new Vector4(outlinePx, 0.3f, 0, 0));
            cmd.SetGlobalVector(ScreenSizeId, new Vector4(target.width, target.height, 0, 0));
            var model = ModelMatrix(x, y, size);
            Covered = CoveredBy(model, outlinePx * width / target.width);
            foreach (var part in parts)
            {
                var data = part.Data;
                // 1. opaque and alpha-tested meshes 2. the face plane 3. alpha-blended sheets (front
                // hair, plates, glints) last, so their clear texels do not hide the face behind them.
                for (int stage = 0; stage < 3; stage++)
                for (int p = 0; p < part.Meshes.Length; p++)
                {
                    int mat = data.Primitives[p].Material;
                    if (mat < 0 || mat >= part.Materials.Length) continue;
                    var info = data.Materials[mat];
                    int at = info.Face ? 1 : info.Blend ? 2 : 0;
                    if (at == stage) cmd.DrawMesh(part.Meshes[p], model, part.Materials[mat], 0, 0);
                }
            }
            // The black line: back faces pushed out along their normals, under the model.
            foreach (var part in parts)
                for (int p = 0; p < part.Meshes.Length; p++)
                {
                    int mat = part.Data.Primitives[p].Material;
                    if (mat < 0 || mat >= part.Materials.Length || part.Data.Materials[mat].Face) continue;
                    cmd.DrawMesh(part.Meshes[p], model, part.Materials[mat], 0, 1);
                }
            Dirty = false;
        }

        // The area the meshes fall on as posed now: the model matrix puts a vertex at its place in
        // the area (the camera only looks down z), so it is the bounds of every posed vertex (the
        // meshes' own bounds are a fixed box that keeps them from being culled), widened by the
        // black line pushed out of them. A few thousand vertices, once a redraw.
        Rect CoveredBy(Matrix4x4 model, float outline)
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (var part in parts)
                foreach (var positions in part.Positions)
                    foreach (var position in positions)
                    {
                        var at = model.MultiplyPoint3x4(position);
                        if (at.x < left) left = at.x;
                        if (at.x > right) right = at.x;
                        if (at.y < top) top = at.y;
                        if (at.y > bottom) bottom = at.y;
                    }
            return left > right ? default : Rect.MinMaxRect(left - outline, top - outline, right + outline, bottom + outline);
        }

        static readonly int OutlineId = Shader.PropertyToID("_DonOutline");
        static readonly int ScreenSizeId = Shader.PropertyToID("_DonScreenSize");
    }
}
