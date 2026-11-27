namespace OurTaiko
{
    // The layers a player wears over their nameplate, each one .otanim drawn on the whole plate
    // canvas (NameplateLayout.Width x Height; the otanim folder's SPEC.md, nameplate layers):
    // the title band, an effect over it (both cut to the band's shape) and an icon over the
    // outline. An item's id is "<slot folder>/<name>", the path of its file under the items folder.
    public enum NameplateSlot { Band, Effect, Icon }

    // What a title is made of: the id of the item it wears in each slot, null where it wears none,
    // and whether its effect is drawn over the whole plate (EffectOverflow) instead of being cut to
    // the band, which is the choice of whoever made the title. A title the site gives also says
    // which picture each part is, by the SHA-256 of its file (BandHash, …; null where it does not
    // say): the part is then drawn from that picture, whatever the set in use knows of its name.
    // A title is these layers and nothing more, so a plate can wear any slot of it on its own.
    public readonly struct TitleParts
    {
        public readonly string Band, Effect, Icon;
        public readonly bool EffectOverflow;
        public readonly string BandHash, EffectHash, IconHash;

        public TitleParts(string band, string effect, string icon, bool effectOverflow = false)
            : this(band, effect, icon, effectOverflow, null, null, null) { }

        TitleParts(string band, string effect, string icon, bool effectOverflow, string bandHash, string effectHash, string iconHash)
        {
            Band = band;
            Effect = effect;
            Icon = icon;
            EffectOverflow = effectOverflow;
            BandHash = bandHash;
            EffectHash = effectHash;
            IconHash = iconHash;
        }

        // These parts, each told by the picture of that hash (null: by its name alone); a slot that
        // wears nothing has none.
        public TitleParts WithHashes(string band, string effect, string icon) =>
            new TitleParts(Band, Effect, Icon, EffectOverflow, Band == null ? null : band, Effect == null ? null : effect, Icon == null ? null : icon);

        public string HashIn(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Band => BandHash,
            NameplateSlot.Effect => EffectHash,
            _ => IconHash,
        };

        public string In(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Band => Band,
            NameplateSlot.Effect => Effect,
            _ => Icon,
        };
    }

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
