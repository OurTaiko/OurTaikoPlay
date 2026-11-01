using System;
using UnityEngine;

namespace OurTaiko
{
    public enum AudioGroup { Effects, Bgm, Track, Drum, Voice }
    public enum SoundPlatform { Desktop, Windows, Android, IOS }

    [Serializable]
    public sealed class SoundVolumes
    {
        // Multipliers preserve the authored mix when loading an older settings file.
        public float master = 1, bgm = 1, track = 1, drum = 1, effects = 1, voice = 1;
        static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp(value, 0, 2);
        public float Gain(AudioGroup group) => Safe(master) * Safe(group switch
        {
            AudioGroup.Bgm => bgm, AudioGroup.Track => track, AudioGroup.Drum => drum,
            AudioGroup.Voice => voice, _ => effects,
        });
    }

    public static class SoundSettings
    {
        public static SoundPlatform Platform
        {
            get
            {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return SoundPlatform.Windows;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return SoundPlatform.Android;
#elif UNITY_IOS && !UNITY_EDITOR
                return SoundPlatform.IOS;
#else
                return SoundPlatform.Desktop;
#endif
            }
        }
    }
}
