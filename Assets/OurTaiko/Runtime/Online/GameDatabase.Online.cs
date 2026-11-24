using OurTaiko.Online;
using SQLite;

namespace OurTaiko
{
    // OurTaiko.Online's tables in scores.sqlite3, as GameDatabase's steps create and change them.
    // They live here so that Core, which owns the file, does not refer to Online.
    public sealed partial class GameDatabase
    {
        static partial void CreateOnlineTables(SQLiteConnection db)
        {
            db.CreateTable<PendingScoreQueue.Entry>();
            db.CreateTable<PendingDanQueue.Entry>();
            db.CreateTable<OfflineChartStore.Entry>();
            db.CreateTable<AudioLevelStore.Entry>();
        }
    }
}
