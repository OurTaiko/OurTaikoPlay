using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const float DancerFrameRate = 8;

        // Replaces each dancer's SpriteFlipbook with Dancer.anim, keeping the frames it held.
        [MenuItem("OurTaiko/Apply Dancer Clip")]
        public static void ApplyDancerClip()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var root = play.judgment.canvas.transform;
            var dancers = Enumerable.Range(1, 5).Select(i => FindDeep(root, "Dancer" + i)).ToArray();
            var clip = DancerClip(Enumerable.Range(0, 19).Select(i => Sprite("background/dancer/dancer_0/0_loop/" + i)).ToArray());
            play.dancers = dancers.Select(dancer =>
            {
                foreach (var old in dancer.GetComponents<MonoBehaviour>().Where(c => c == null || c.GetType().Name == "SpriteFlipbook"))
                    UnityEngine.Object.DestroyImmediate(old);
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(dancer.gameObject);
                return AttachClip(dancer.gameObject, clip);
            }).ToArray();
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // Dancer.anim: background/dancer/dancer_0/0_loop at 8 fps, looping.
        static AnimationClip DancerClip(Sprite[] frames) => SaveClip("Dancer", DancerFrameRate, true, clip =>
            SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), frames, frames.Select((_, i) => i / DancerFrameRate).ToArray()));

        // Adds (or reuses) the Animator and ClipSampler that play a clip on this object.
        static ClipSampler AttachClip(GameObject target, AnimationClip clip)
        {
            if (target.GetComponent<Animator>() == null) target.AddComponent<Animator>();
            var sampler = target.GetComponent<ClipSampler>();
            if (sampler == null) sampler = target.AddComponent<ClipSampler>();
            sampler.clip = clip;
            EditorUtility.SetDirty(sampler);
            return sampler;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException(name + " is missing.");
        }
    }
}
