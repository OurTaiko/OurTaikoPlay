using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // A player's vote on a chart, the website's own up and down vote
    // (GET and PUT /api/v2/game/charts/{id}/vote).
    public sealed partial class OnlineClient
    {
        // The seat's vote on the chart: 1, -1, or 0 for none yet. null when it cannot be told: the
        // server is not connected or the seat not signed in there, the server has no such call, or
        // the request failed.
        public async Task<int?> MyVoteAsync(OnlineChart chart, int seat = 0, CancellationToken cancel = default)
        {
            var endpoint = chart != null ? ConnectedEndpoint(chart.Server) : null;
            if (endpoint == null || !endpoint.Seats[seat].IsAuthenticated) return null;
            try
            {
                var reply = Json.Parse(await endpoint.AuthorizedAsync(VotePath(chart), cancel: cancel, seat: seat));
                return reply["myVote"] is JValue value && value.Type == JTokenType.Integer ? (int)value : (int?)null;
            }
            catch (System.Exception error) when (error is OnlineException || error is HttpStatusException || error is Newtonsoft.Json.JsonException)
            {
                Log.Debug("Online", "Vote of " + chart.Id + " not read: " + error.Message);
                return null;
            }
        }

        // Gives the seat's vote (1 or -1; 0 withdraws it). False when it did not reach the server.
        public async Task<bool> VoteAsync(OnlineChart chart, int value, int seat = 0, CancellationToken cancel = default)
        {
            var endpoint = chart != null ? ConnectedEndpoint(chart.Server) : null;
            if (endpoint == null || !endpoint.Seats[seat].IsAuthenticated) return false;
            try
            {
                await endpoint.AuthorizedAsync(VotePath(chart), new JObject { ["value"] = value }.ToString(Newtonsoft.Json.Formatting.None),
                    cancel: cancel, seat: seat, method: HttpMethod.Put);
                return true;
            }
            catch (System.Exception error) when (error is OnlineException || error is HttpStatusException)
            {
                Log.Warn("Online", "Vote on " + chart.Id + " not sent: " + error.Message);
                return false;
            }
        }

        static string VotePath(OnlineChart chart) => "/api/v2/game/charts/" + System.Uri.EscapeDataString(chart.Id) + "/vote";
    }
}
