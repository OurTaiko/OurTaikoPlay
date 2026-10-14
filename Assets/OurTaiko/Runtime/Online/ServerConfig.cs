using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OurTaiko.Online
{
    // One [[network.servers]] entry of OurTaikoPlayer's config: the API root, the account and an
    // optional HTTP proxy. An empty proxy disables proxies, including the environment's.
    [Serializable]
    public sealed class ServerConfig
    {
        public bool enabled = true;
        public string name = "", baseUrl = "", username = "", password = "", httpProxy = "";
        // ServerLogin logs in on its own while the remembered account last succeeded.
        public bool autoLogin;

        public ServerConfig Clone() => (ServerConfig)MemberwiseClone();
        public bool HasCredentials => !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        public string DisplayName => string.IsNullOrEmpty(name) ? baseUrl : name;
    }

    // servers.json in the persistent data folder: the additional servers, each with the account
    // ServerLogin filled in. The built-in servers (OurTaiko Fanmade and OurTaiko Database) are fixed
    // here and never written to the file; they use the OurTaiko account of the settings.
    [Serializable]
    public sealed class ServerList
    {
        public const string FileName = "servers.json";
        public static readonly (string Name, string Url)[] BuiltIn =
        {
            ("OurTaiko Fanmade", "https://fanmade.ourtaiko.org"),
            ("OurTaiko Database", "https://database.ourtaiko.org"),
        };

        public List<ServerConfig> servers = new List<ServerConfig>();

        public static bool IsBuiltIn(string baseUrl) =>
            Array.Exists(BuiltIn, b => string.Equals(b.Url, (baseUrl ?? "").Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

        // Drops the built-in addresses older versions listed in the file. True when one was removed.
        public bool RemoveBuiltIn() => servers.RemoveAll(s => IsBuiltIn(s.baseUrl)) > 0;

        public static ServerList Read(string path)
        {
            if (!File.Exists(path)) return null;
            var list = JsonUtility.FromJson<ServerList>(File.ReadAllText(path)) ?? new ServerList();
            list.servers ??= new List<ServerConfig>();
            list.servers.RemoveAll(server => server == null);
            return list;
        }

        public void Write(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
