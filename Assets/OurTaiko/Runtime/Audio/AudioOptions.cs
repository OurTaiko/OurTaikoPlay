using System;

namespace OurTaiko
{
    public enum AudioBackend { Automatic, Bass, Wasapi, Asio, Unity }

    [Serializable]
    public sealed class AudioOptions
    {
        public AudioBackend backend = AudioBackend.Automatic;
        public int sampleRate = 44100;
        // MajdataPlay's mobile defaults. Desktop uses a 16 ms device period / 64 ms buffer.
        public int devicePeriodMs = 0;
        public int deviceBufferMs = 0;
        public bool androidAAudio = true;
        public bool wasapiExclusive = true;
        public bool wasapiRaw = true;
        public bool wasapiAsync = true;
        public float wasapiBufferSeconds = 0.02f;
        public float wasapiPeriodSeconds = 0.005f;
        public int asioDevice;
        public int asioBufferSamples;

        public int Rate => sampleRate >= 8000 && sampleRate <= 192000 ? sampleRate : 44100;
        public int Period(bool mobile) => Math.Clamp(devicePeriodMs == 0 ? (mobile ? 8 : 16) : devicePeriodMs, 1, 100);
        public int Buffer(bool mobile) => Math.Clamp(deviceBufferMs == 0 ? (mobile ? 32 : 64) : deviceBufferMs, Period(mobile) * 2, 1000);
    }
}
