using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OurTaiko
{
    // The layered set plates are drawn from: the catalog (the plate's layouts, and the looks to
    // start from: "titles" and "aiTitles", each {band, effect?, icon?, effectOverflow?}) and a file
    // for each item, by its id ("band/lumen-title-gold", "composition/plates"). The game does not
    // carry it: a player's is what the site says there is (Online.NameplateDownloads, each file in
    // the object cache); the Editor's preview and the tests read the folder
    // Tools/nameplate-samples/lumen bakes (FromFolder, Baked).
    public sealed class NameplateSet
    {
        public const string CatalogId = "composition/catalog";
        // The folders of the items, as an id begins.
        public static readonly string[] Folders = { "band", "effect", "icon", "composition" };

        public readonly JObject Catalog;
        readonly Dictionary<string, string> files;

        // `files`: where each item's .otanim is on disk, by id.
        public NameplateSet(JObject catalog, IEnumerable<KeyValuePair<string, string>> files)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files) this.files[file.Key] = file.Value;
        }

        // Where the item's file is; null when the set has no such item.
        public string PathOf(string id) => id != null && files.TryGetValue(id, out var path) ? path : null;

        // A baked folder: <folder>/<name>.otanim for each item and composition/catalog.json. The
        // baked catalog says which effects run outside the band by a list (unclipped), as the arcade's
        // titles draw them; here each of its titles says so itself.
        public static NameplateSet FromFolder(string folder)
        {
            var catalog = JObject.Parse(File.ReadAllText(Path.Combine(folder, CatalogId + ".json")));
            if (catalog["unclipped"] is JArray unclipped)
            {
                var over = new HashSet<string>(StringComparer.Ordinal);
                foreach (var effect in unclipped) over.Add((string)effect);
                foreach (string titles in new[] { "titles", "aiTitles" })
                    foreach (var title in (catalog[titles] as JObject)?.Properties() ?? new JProperty[0])
                        if (title.Value is JObject layers && over.Contains((string)layers["effect"] ?? "")) layers["effectOverflow"] = true;
                catalog.Remove("unclipped");
            }
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sub in Folders)
            {
                string dir = Path.Combine(folder, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (string path in Directory.GetFiles(dir, "*" + NameplateItems.Extension))
                    found[sub + "/" + Path.GetFileNameWithoutExtension(path)] = path;
            }
            return new NameplateSet(catalog, found);
        }

#if UNITY_EDITOR
        // Where the set is baked, and published from (OurTaikoNet tools/nameplate/publish-lumen.sh):
        // the Editor's preview and the tests draw it. It is not in the repository, which keeps the
        // arcade's pictures out: bake it there (Tools/nameplate-samples/lumen/bake.py) or fetch the
        // published one (fetch.py beside it).
        public static string BakedFolder => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Tools", "nameplate-samples", "lumen", "set"));

        // The baked set; null when the project has none.
        public static NameplateSet Baked() => File.Exists(Path.Combine(BakedFolder, CatalogId + ".json")) ? FromFolder(BakedFolder) : null;
#endif
    }
}
