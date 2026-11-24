using System.Collections.Generic;

namespace OurTaiko
{
    // A course's song as the 段位 select found it (DanCourseResolver).
    public sealed class DanResolvedSong
    {
        public DanSongRef Ref;
        public SongDefinition Song;
        // The block the course plays: its TJA COURSE value as written and its side (ChartSide), for
        // SongDefinition.ParseBlock, and its level. A course pins a server song's side; otherwise it
        // is the side the 1P seat reads, found once here so the run never re-chooses by seat.
        public string Course;
        public string Side = "single";
        public int Level;
        // The played block's content hash on a server's song (its identity for records); empty locally.
        public string ChartHash = "";
        // The course by its canonical name (Easy..Edit), which the soul gauge tables know.
        public string GaugeCourse => SongInfo.CourseName(Ref.Difficulty);
    }

    // A course with its songs found, or the reason it cannot be played (shown greyed out).
    public sealed class DanCourseEntry
    {
        public DanCourse Course;
        public string Path;
        public readonly List<DanResolvedSong> Songs = new List<DanResolvedSong>();
        public string Error;
        // The 外伝 entry at the end of an online page: not a course, but where a code is typed.
        public bool IsGaiden;
        public bool IsPlayable => Error == null;
    }
}
