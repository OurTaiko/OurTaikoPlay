namespace OurTaiko.Online
{
    // One game server: what it is called, the address it is known by, and where its API is. The
    // game connects to the built-in servers only; players sign in to them with their OurTaiko
    // accounts.
    public sealed class ServerConfig
    {
        // The one site both built-in servers are part of since 2026-11-14, each as a song source.
        public const string Site = "https://net.ourtaiko.org";

        // Url is the address each had as a site of its own. A server's charts, offline cache,
        // queued scores and saved lists are kept under it (FanmadeEndpoint.Id), so it stays what
        // it was and nothing a player has moves. Api is where its API is now.
        public static readonly (string Name, string Url, string Api)[] BuiltIn =
        {
            ("OurTaiko Fanmade", "https://fanmade.ourtaiko.org", Site + "/api/v1/sources/fanmade"),
            ("OurTaiko Database", "https://database.ourtaiko.org", Site + "/api/v1/sources/official"),
        };

        const string OwnRoot = "/api/v1";

        // api is empty for a server whose API is at its own address, under /api/v1.
        public string name = "", baseUrl = "", api = "";

        public string DisplayName => string.IsNullOrEmpty(name) ? baseUrl : name;

        // The address requests are sent under: api, or the server's own.
        public string ApiRoot => string.IsNullOrEmpty(api) ? baseUrl + OwnRoot : api;

        // Where a request is sent. path is written as the server's own ("/api/v1/game/catalog").
        public string Address(string path)
        {
            path = path ?? "";
            if (string.IsNullOrEmpty(api) || !path.StartsWith(OwnRoot, System.StringComparison.Ordinal)) return baseUrl + path;
            return api + path.Substring(OwnRoot.Length);
        }
    }
}
