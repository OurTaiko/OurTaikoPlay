using System;

namespace OurTaiko
{
    // Values are persisted as integers; 4 was the removed Unity output and now reads as Automatic.
    // Every backend decodes and mixes with BASS; the name says how it outputs. BassSimple is BASS's own
    // cross-platform output (through the system mixer); BassWASAPI and BassASIO are Windows-only.
    public enum AudioBackend { Automatic, BassSimple, BassWASAPI, BassASIO }

    [Serializable]
    public sealed class AudioOptions
    {
        public AudioBackend backend = AudioBackend.Automatic;
        public int sampleRate = 44100;
        public SoundVolumes volume = new SoundVolumes();
        public int updatePeriodMs = 100, playbackBufferMs = 1000;
        // 0 = platform default: MajdataPlay's 8 ms period on mobile (16 ms on desktop); buffer 16 ms on
        // Android (MajdataPlay's recommended setting), 32 ms on iOS and 64 ms on desktop.
        public int devicePeriodMs = 0;
        public int deviceBufferMs = 0;
        public bool androidAAudio = true;
        public bool wasapiExclusive = true;
        public bool wasapiRaw = true;
        public bool wasapiAsync = true;
        public float wasapiBufferSeconds = 0.006f;
        // 0 lets the driver choose its default period.
        public float wasapiPeriodSeconds = 0;
        public int asioDevice;
        public int asioBufferSamples;

        public bool SameDeviceSettings(AudioOptions b) => b != null && backend == b.backend && sampleRate == b.sampleRate
            && updatePeriodMs == b.updatePeriodMs && playbackBufferMs == b.playbackBufferMs
            && devicePeriodMs == b.devicePeriodMs && deviceBufferMs == b.deviceBufferMs && androidAAudio == b.androidAAudio
            && wasapiExclusive == b.wasapiExclusive && wasapiRaw == b.wasapiRaw && wasapiAsync == b.wasapiAsync
            && wasapiBufferSeconds == b.wasapiBufferSeconds && wasapiPeriodSeconds == b.wasapiPeriodSeconds
            && asioDevice == b.asioDevice && asioBufferSamples == b.asioBufferSamples;

        public AudioBackend Backend => Enum.IsDefined(typeof(AudioBackend), backend) ? backend : AudioBackend.Automatic;
        public int Rate => sampleRate >= 8000 && sampleRate <= 192000 ? sampleRate : 44100;
        static bool Mobile(SoundPlatform platform) => platform == SoundPlatform.Android || platform == SoundPlatform.IOS;
        public int Period(SoundPlatform platform) => Math.Clamp(devicePeriodMs == 0 ? (Mobile(platform) ? 8 : 16) : devicePeriodMs, 1, 100);
        public int Buffer(SoundPlatform platform) => Math.Clamp(deviceBufferMs == 0
            ? (platform == SoundPlatform.Android ? 16 : Mobile(platform) ? 32 : 64) : deviceBufferMs, Period(platform) * 2, 1000);
    }
}
