using System;

namespace OurTaiko
{
    // A song's audio decoded ahead of its play (SongDefinition.SetPreparedAudio). The audio layer's
    // NativeAudioSample is the one kind; the song only holds it and disposes of what nobody took.
    public interface IPreparedAudio : IDisposable
    {
        bool IsDisposed { get; }
    }
}
