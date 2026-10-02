using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OurTaiko
{
    // Applies one AnimationClip at a time chosen by code, through a manually evaluated
    // Playables graph (AnimationClip.SampleAnimation does not apply sprite keys).
    [ExecuteAlways]
    [RequireComponent(typeof(Animator))]
    public sealed class ClipSampler : MonoBehaviour
    {
        PlayableGraph graph;
        AnimationClipPlayable playable;

        public static ClipSampler Attach(GameObject target, AnimationClip clip)
        {
            var sampler = target.AddComponent<ClipSampler>();
            sampler.graph = PlayableGraph.Create(target.name + " Clip");
            sampler.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(sampler.graph, "Clip", sampler.GetComponent<Animator>());
            sampler.playable = AnimationClipPlayable.Create(sampler.graph, clip);
            output.SetSourcePlayable(sampler.playable);
            return sampler;
        }

        public void Sample(double seconds)
        {
            playable.SetTime(seconds);
            graph.Evaluate();
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
