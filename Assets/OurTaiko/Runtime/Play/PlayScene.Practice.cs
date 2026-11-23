namespace OurTaiko
{
    // Where the play scene picks its PlayMode. Play.Practice carries a PracticeView, so it plays
    // PracticeMode; every other play scene plays normally.
    public sealed partial class PlayScene
    {
        public PracticeView practiceView;
        public bool IsPractice => practiceView != null;
        // The practice menu and its attempts; null outside practice.
        public PracticeMode Practice { get; private set; }
        PlayMode mode = PlayMode.Normal;

        // First thing in Start: a direct Editor run must return to the same mode after Back and another song.
        void ChooseMode()
        {
            switcher.PracticeMode = IsPractice;
            if (IsPractice) mode = Practice = new PracticeMode(this, practiceView, switcher);
        }
    }
}
