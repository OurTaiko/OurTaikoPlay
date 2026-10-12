using System;

namespace OurTaiko
{
    // A song-select folder: a local box.def folder or a server category. Songs open inline under it.
    public sealed class SongFolder
    {
        public string Key = "", Title = "", ServerName = "";
        public int Genre;
        public SongDefinition[] Songs = Array.Empty<SongDefinition>();
        // A playlist's own picture for the open folder; null without one or until it has loaded.
        public UnityEngine.Sprite Cover;
    }
}
