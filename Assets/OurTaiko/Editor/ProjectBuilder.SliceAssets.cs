using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        // Moves every remaining per-frame sprite asset in Generated (the old Slice output) into its
        // sheet's importer: the same names and rects become sub-sprites of the PNG, every reference in
        // prefabs, scenes, clips and other assets is remapped through SerializedObject, and the old
        // assets are deleted once nothing points at them.
        [MenuItem("OurTaiko/Move Slices Into Sheets")]
        public static void MoveSlicesIntoSheets()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing assets.");
            var old = AssetDatabase.FindAssets("t:Sprite", new[] { Root + "Generated" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".asset"))
                .Select(p => (path: p, sprite: AssetDatabase.LoadAssetAtPath<Sprite>(p))).Where(e => e.sprite != null).ToList();
            var map = new Dictionary<Sprite, Sprite>();
            foreach (var sheet in old.GroupBy(e => AssetDatabase.GetAssetPath(e.sprite.texture)))
            {
                string art = sheet.Key.Substring((Root + "Art/").Length, sheet.Key.Length - (Root + "Art/").Length - ".png".Length);
                int height = sheet.First().sprite.texture.height;
                var cells = sheet.Select(e =>
                {
                    var r = e.sprite.rect;
                    return (e.sprite.name, (int)r.x, height - (int)r.y - (int)r.height, (int)r.width, (int)r.height);
                }).ToArray();
                var sliced = SliceSheet(art, cells);
                foreach (var (entry, sprite) in sheet.Zip(sliced, (e, s) => (e, s))) map[entry.sprite] = sprite;
            }

            // Prefabs first, so scene instances match their (already remapped) prefabs.
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { if (RemapAll(root, map)) PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path);
                bool changed = false;
                foreach (var root in scene.GetRootGameObjects()) changed |= RemapAll(root, map);
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            foreach (var path in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    bool changed = false;
                    for (int i = 0; i < keys.Length; i++)
                        if (keys[i].value is Sprite s && map.TryGetValue(s, out var next)) { keys[i].value = next; changed = true; }
                    if (changed) AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                }
            }
            var oldPaths = new HashSet<string>(old.Select(e => e.path));
            foreach (var path in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !oldPaths.Contains(p) && p.EndsWith(".asset")).Distinct())
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).Where(a => a != null && !(a is Sprite)))
                    if (Remap(asset, map)) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // Delete only what no asset references any more (text scan of serialized files).
            var guids = old.ToDictionary(e => AssetDatabase.AssetPathToGUID(e.path), e => e.path);
            var still = new HashSet<string>();
            foreach (var file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".unity") || f.EndsWith(".prefab") || f.EndsWith(".asset") || f.EndsWith(".anim") || f.EndsWith(".mat")))
            {
                string path = file.Replace('\\', '/');
                if (oldPaths.Contains(path)) continue;
                string text = File.ReadAllText(path);
                foreach (var guid in guids.Keys) if (text.Contains(guid)) still.Add(guids[guid] + " <- " + path);
            }
            if (still.Count > 0) throw new InvalidOperationException("Still referenced, nothing deleted:\n" + string.Join("\n", still));
            foreach (var path in oldPaths) AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            Debug.Log($"OurTaiko: moved {old.Count} sprite slices into their sheets.");
        }

        static bool RemapAll(GameObject root, Dictionary<Sprite, Sprite> map)
        {
            bool changed = false;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null) changed |= Remap(component, map);
            return changed;
        }

        static bool Remap(UnityEngine.Object target, Dictionary<Sprite, Sprite> map)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.GetIterator();
            bool changed = false;
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue is Sprite sprite && map.TryGetValue(sprite, out var next))
                {
                    property.objectReferenceValue = next;
                    changed = true;
                }
            if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }
    }
}
