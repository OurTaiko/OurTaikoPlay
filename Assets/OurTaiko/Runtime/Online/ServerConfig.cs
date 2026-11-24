namespace OurTaiko.Online
{
    // One game server: what it is called, the address it is known by, and where its API is. The
    // game connects to the built-in servers only; players sign in to them with their OurTaiko
    // accounts.
    public sealed class ServerConfig
    {
        // The one site both built-in servers are part of since 2026-11-14, each as a song source.
        public const string Site = "https://net.ourtaiko.org";
        public const string SiteName = "OurTaikoNET";

        // Url is the address each had as a site of its own. A server's charts, offline cache,
        // queued scores and saved lists are kept under it (OnlineEndpoint.Id), so it stays what
        // it was and nothing a player has moves. Api is where its API is now, and Site the site it
        // is a source of: the servers of one site are loaded and signed in to together
        // (GET <site>/api/v2/game/sources, OnlineClient.LoadSiteAsync). The game speaks version 2
        // of the site's API, which names public files by their path (Distribution); version 1
        // stays as it was for the games released before.
        public static readonly (string Name, string Url, string Api, string Site)[] BuiltIn =
        {
            ("OurTaiko Fanmade", "https://fanmade.ourtaiko.org", Site + "/api/v2/sources/fanmade", Site),
            ("OurTaiko Database", "https://database.ourtaiko.org", Site + "/api/v2/sources/official", Site),
        };

        public const string OwnRoot = "/api/v2";

        // api is empty for a server whose API is at its own address, under /api/v2; site, for one
        // that is no source of a site and is loaded by itself.
        public string name = "", baseUrl = "", api = "", site = "";

        // What the site calls this source: the last part of its API address.
        public string SourceId => string.IsNullOrEmpty(api) ? "" : api.Substring(api.LastIndexOf('/') + 1);

        public string DisplayName => string.IsNullOrEmpty(name) ? baseUrl : name;

        // The address requests are sent under: api, or the server's own.
        public string ApiRoot => string.IsNullOrEmpty(api) ? baseUrl + OwnRoot : api;

        // Where a request is sent. path is written as the server's own ("/api/v2/game/catalog").
        public string Address(string path)
        {
            path = path ?? "";
            if (string.IsNullOrEmpty(api) || !path.StartsWith(OwnRoot, System.StringComparison.Ordinal)) return baseUrl + path;
            return api + path.Substring(OwnRoot.Length);
        }
    }
}
