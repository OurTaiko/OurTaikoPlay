using UnityEngine;

namespace OurTaiko.Chara
{
    // A .glb file as it is on disk (imported by OurTaiko.Editor's GlbImporter), so the models under
    // Resources/Chara stay glb and load the same way on every platform. Glb reads it.
    public sealed class GlbAsset : ScriptableObject
    {
        public byte[] bytes;
    }
}
