namespace OurTaiko
{
    // The layers a player wears over their nameplate, each one .otanim drawn on the whole plate
    // canvas (NameplateLayout.Width x Height; the otanim folder's SPEC.md, nameplate layers):
    // the title band, an effect over it (both cut to the band's shape) and an icon over the
    // outline. An item's id is "<slot folder>/<name>", the path of its file under the items folder.
    public enum NameplateSlot { Band, Effect, Icon }

    public static class NameplateSlots
    {
        public static readonly NameplateSlot[] All = { NameplateSlot.Band, NameplateSlot.Effect, NameplateSlot.Icon };

        public static string Folder(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Band => "band",
            NameplateSlot.Effect => "effect",
            _ => "icon",
        };

        public static string Id(NameplateSlot slot, string name) => Folder(slot) + "/" + name;

        // An item's slot and name, for an id naming a file of its slot's folder and nothing else.
        public static bool TryParse(string id, out NameplateSlot slot, out string name)
        {
            slot = default;
            name = null;
            if (string.IsNullOrEmpty(id)) return false;
            int cut = id.IndexOf('/');
            if (cut <= 0) return false;
            string folder = id.Substring(0, cut);
            name = id.Substring(cut + 1);
            if (!IsName(name)) { name = null; return false; }
            foreach (var s in All)
                if (Folder(s) == folder) { slot = s; return true; }
            name = null;
            return false;
        }

        // A file name: no folders, no way out of the slot's folder.
        public static bool IsName(string name) =>
            !string.IsNullOrEmpty(name) && name.Trim() == name && name != "." && name != ".."
            && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0;
    }
}
