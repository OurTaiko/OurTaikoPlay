using System;
using UnityEngine;

namespace OurTaiko
{
    // The same authored AnimationClip / Playables path as the other play effects.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class EndingView : MonoBehaviour
    {
        public AnimationClip fail, clear, fullCombo, donderful;
        public AudioClip failSound, clearSound, fullComboSound, donderfulSound;
        public AudioSource audioSource;
        ClipSampler sampler;
        public double Duration => sampler != null && sampler.clip != null ? sampler.clip.length : 0;

        public void Begin(PlayResult result)
        {
            sampler = GetComponent<ClipSampler>();
            gameObject.SetActive(true);
            var crown = result.ResultCrown;
            sampler.Play(crown == Crown.DonderfulCombo ? donderful : crown == Crown.FullCombo ? fullCombo
                : crown == Crown.Clear ? clear : fail, 0);
            var sound = crown == Crown.DonderfulCombo ? donderfulSound : crown == Crown.FullCombo ? fullComboSound
                : crown == Crown.Clear ? clearSound : failSound;
            if (audioSource != null && sound != null) audioSource.PlayAudioOneShot(sound);
        }

        public void ShowTime(double seconds) => sampler.Sample(Math.Min(Math.Max(0, seconds), Duration));
    }
}
