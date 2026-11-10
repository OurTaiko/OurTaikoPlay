using System;

namespace OurTaiko
{
    // Values are persisted as integers; 4 was the removed Unity output and now reads as Automatic.
    // Every backend decodes and mixes with BASS; the name says how it outputs. BassSimple is BASS's own
    // cross-platform output (through the system mixer); BassWASAPI and BassASIO are Windows-only.
    public enum AudioBackend { Automatic, BassSimple, BassWASAPI, BassASIO }

    // What the song clock reads while the music plays. Heard: the music coming out of the speakers now
    // (the picture matches what is heard). Sent: the music handed to the device now, heard one output
    // latency later (a sound started on the clock is heard with the music). Automatic keeps what each
    // backend always did: Heard on BassSimple, Sent on BassWASAPI and BassASIO.
    public enum AudioClock { Automatic, Heard, Sent }

    // How the clock choice is carried out for one output, in real seconds.
    public readonly struct ClockTiming
    {
        // Autoplay starts its drum sounds this long before the judgment (PlayerLane.SoundAhead).
        public readonly double SoundLead;
        // BassSimple: added to the stream position the clock is pulled to (the position is what is heard).
        public readonly double PositionLead;
        // BassWASAPI, BassASIO: the clock is set back this far when the track starts (it starts as sent).
        public readonly double StartDelay;

        public ClockTiming(bool heard, AudioBackend backend, double latency)
        {
            latency = Math.Max(0, latency);
            bool positional = backend == AudioBackend.BassSimple;
            SoundLead = heard ? latency : 0;
            PositionLead = !heard && positional ? latency : 0;
            StartDelay = heard && !positional ? latency : 0;
        }
    }

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
        // Not a device setting: read every frame, so a change needs no reopening of the output.
        public AudioClock clock = AudioClock.Automatic;

        public bool SameDeviceSettings(AudioOptions b) => b != null && backend == b.backend && sampleRate == b.sampleRate
            && updatePeriodMs == b.updatePeriodMs && playbackBufferMs == b.playbackBufferMs
            && devicePeriodMs == b.devicePeriodMs && deviceBufferMs == b.deviceBufferMs && androidAAudio == b.androidAAudio
            && wasapiExclusive == b.wasapiExclusive && wasapiRaw == b.wasapiRaw && wasapiAsync == b.wasapiAsync
            && wasapiBufferSeconds == b.wasapiBufferSeconds && wasapiPeriodSeconds == b.wasapiPeriodSeconds
            && asioDevice == b.asioDevice && asioBufferSamples == b.asioBufferSamples;

        public AudioBackend Backend => Enum.IsDefined(typeof(AudioBackend), backend) ? backend : AudioBackend.Automatic;
        public AudioClock Clock => Enum.IsDefined(typeof(AudioClock), clock) ? clock : AudioClock.Automatic;
        // `actual` is the output that opened (AudioEngine.Backend), never Automatic.
        public bool HeardClock(AudioBackend actual) => Clock == AudioClock.Heard || Clock == AudioClock.Automatic && actual == AudioBackend.BassSimple;
        public int Rate => sampleRate >= 8000 && sampleRate <= 192000 ? sampleRate : 44100;
        static bool Mobile(SoundPlatform platform) => platform == SoundPlatform.Android || platform == SoundPlatform.IOS;
        public int Period(SoundPlatform platform) => Math.Clamp(devicePeriodMs == 0 ? (Mobile(platform) ? 8 : 16) : devicePeriodMs, 1, 100);
        public int Buffer(SoundPlatform platform) => Math.Clamp(deviceBufferMs == 0
            ? (platform == SoundPlatform.Android ? 16 : Mobile(platform) ? 32 : 64) : deviceBufferMs, Period(platform) * 2, 1000);
    }
}
