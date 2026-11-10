namespace OurTaiko.Online
{
    // One game server: what it is called and its API root. The game connects to the built-in
    // servers only; players sign in to them with their OurTaiko accounts.
    public sealed class ServerConfig
    {
        public static readonly (string Name, string Url)[] BuiltIn =
        {
            ("OurTaiko Fanmade", "https://fanmade.ourtaiko.org"),
            ("OurTaiko Database", "https://database.ourtaiko.org"),
        };

        public string name = "", baseUrl = "";

        public string DisplayName => string.IsNullOrEmpty(name) ? baseUrl : name;
    }
}
