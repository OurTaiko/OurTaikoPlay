using System.Threading.Tasks;
using OurTaiko.Multi;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko
{
    // "How was this chart?": the player's vote on the chart just played, asked once when they
    // leave the result of a play alone (single play, ゴーストバトル, マルチ演奏). It is the website's
    // up or down vote, so it is asked only where it can be given and has not been: a server's
    // chart, the player signed in there, a play of their own (not auto play), and no vote of
    // theirs on it yet. Two players at one screen are not asked.
    public sealed partial class ResultScene
    {
        FanmadeChart ratingChart;
        int ratingSeat;
        Task<int?> myVote;
        bool ratingAsked;

        // The player's vote on the chart has been asked of the server and answered.
        public bool RatingKnown => myVote == null || myVote.IsCompleted;

        // song: the play this entry committed. The reveal gives the answer time to come.
        void PrepareRating(SongDefinition song, PlayResult result2P)
        {
            if (song == null || (result2P != null && !switcher.PlaysGhost)) return;
            var own = switcher.PlaysGhost && switcher.Request.Ghost.PlayerLane == 1 ? switcher.ResultOf(1) : Result;
            var online = OnlineManager.Instance;
            var chart = online != null ? online.ChartOf(song) : null;
            if (own == null || !own.IsRecordable || chart == null) return;
            ratingChart = chart;
            ratingSeat = switcher.Lead.Index();
            myVote = online.Client.MyVoteAsync(chart, ratingSeat);
        }

        // Leaving waits for the answer when there is a vote to ask for. A player who leaves before
        // the server has said whether they voted is not asked.
        bool AskRating()
        {
            if (ratingAsked || myVote == null || !myVote.IsCompletedSuccessfully || myVote.Result != 0) return false;
            ratingAsked = true;
            if (Rating == null)
            {
                Rating = new ChartRatingPrompt(stage, Rate);
                sfx.PrepareAudioEffects(MultiArt.Load().ka);
            }
            Rating.Open(Result.Title);
            return true;
        }

        // The player's own drum: ka moves between the answers, don gives the focused one; Back is
        // "not now".
        void HandleRatingInput()
        {
            if (InputManager.GetKeyDown(InputKey.Back)) { Rating.Choose(ChartRatingPrompt.Skip); return; }
            foreach (var press in InputManager.Players[switcher.Lead.Index()].PressesThisFrame)
            {
                if (press.Key.IsKa()) { Rating.Move(press.Key.IsRight() ? 1 : -1); sfx.PlayAudioOneShot(MultiArt.Load().ka); }
                else Rating.Press();
                break;
            }
        }

        // An answer: the vote goes up in the background (it outlives this scene) and the result is left.
        void Rate(string answer)
        {
            int value = answer == ChartRatingPrompt.Good ? 1 : answer == ChartRatingPrompt.Bad ? -1 : 0;
            if (value != 0) _ = OnlineManager.Instance.Client.VoteAsync(ratingChart, value, ratingSeat);
            sfx.PlayAudioOneShot(don);
            Leave();
        }
    }
}
