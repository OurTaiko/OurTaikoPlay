using System;
using UnityEngine;

namespace OurTaiko
{
    // Local profile and built-in nameplate selection, stored in player.json.
    [Serializable]
    public sealed class PlayerInfo
    {
        // scores.cpp seeds player 1 as ('Don-chan', 'Donder Debut!'); nameplate.lua treats that title as unset.
        public const string DefaultName = "Don-chan", DefaultTitle = "Donder Debut!";
        // dan 0 = 初級 .. 24 = 達人 (the emblem sheets' 25 crops); the game offers 五級 (6) .. 達人 only,
        // so 0..5 count as no 段位 (user decision, 2026-10-22).
        public const int DanCount = 25, DanFirst = 6;

        public string name = DefaultName;
        public string title = DefaultTitle;
        public int dan = -1;      // -1 = no 段位
        public bool gold;         // 金合格: gold emblem text (red pass has white text)
        public int danCombo;      // the emblem's ring, as a song's crown: 0 銀粋, 1 金粋 (full combo), 2 虹粋 (all 良)
        // The layers worn over the plate (NameplateSlots ids, "band/<name>"…); empty wears none.
        public string bandItem = "", effectItem = "", iconItem = "";
        // Whether a custom plate draws its effect over the whole plate instead of cutting it to the band.
        public bool effectOverflow;

        // Built-in composition. "auto" follows this seat, title and rank.
        public string plateState = "auto", plateTitle = "auto", plateAiTitle = "wood", plateBlank = "off", plateBadge = "auto";

        public int nameplateVersion = 1;
        // The title the account wears on the site: the band of six an older site gives (style), and
        // the layers its title is made of (NameplateSlots ids; empty: as its style looks).
        public string profileBand = "wood";
        public string profileBandItem = "", profileEffectItem = "", profileIconItem = "";
        public bool profileEffectOverflow;
        // The pictures of those layers, by hash, when the site said which (empty: by name alone).
        public string profileBandHash = "", profileEffectHash = "", profileIconHash = "";

        // The site's title's layers, when the site gave them.
        public TitleParts? ProfileLayers => string.IsNullOrEmpty(profileBandItem) ? null
            : new TitleParts(profileBandItem, NullIfEmpty(profileEffectItem), NullIfEmpty(profileIconItem), profileEffectOverflow)
                .WithHashes(NullIfEmpty(profileBandHash), NullIfEmpty(profileEffectHash), NullIfEmpty(profileIconHash));

        public void SetProfileLayers(TitleParts? layers)
        {
            profileBandItem = layers?.Band ?? "";
            profileEffectItem = layers?.Effect ?? "";
            profileIconItem = layers?.Icon ?? "";
            profileEffectOverflow = layers?.EffectOverflow ?? false;
            profileBandHash = layers?.BandHash ?? "";
            profileEffectHash = layers?.EffectHash ?? "";
            profileIconHash = layers?.IconHash ?? "";
        }

        static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        public bool HasTitle => !string.IsNullOrEmpty(title) && title != DefaultTitle;
        // An out-of-range dan is "no dan", as nameplate.lua's clamp does.
        public bool HasDan => dan >= DanFirst && dan < DanCount;
        public int DanComboTier => danCombo >= 0 && danCombo <= 2 ? danCombo : 0;
        // Np_coin: no title and no dan. The coin plate never shows a title band or dan chip.
        public bool IsCoin => !HasTitle && !HasDan;

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();

        public string ItemIn(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Band => bandItem,
            NameplateSlot.Effect => effectItem,
            _ => iconItem,
        };

        public void Wear(NameplateSlot slot, string id)
        {
            id ??= "";
            if (slot == NameplateSlot.Band) bandItem = id;
            else if (slot == NameplateSlot.Effect) effectItem = id;
            else iconItem = id;
        }

        // 2P has no account or player file yet (user decision, 2026-10-20): named 2P with the default
        // title, so it wears the plain coin plate like a fresh 1P.
        public static PlayerInfo SecondPlayer => new PlayerInfo { name = "2P" };

        public static PlayerInfo FromJson(string json)
        {
            var info = new PlayerInfo();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, info);
            info.name ??= "";
            info.title ??= "";
            info.bandItem ??= "";
            info.effectItem ??= "";
            info.iconItem ??= "";
            info.profileBandItem ??= "";
            info.profileBandHash ??= "";
            info.profileEffectHash ??= "";
            info.profileIconHash ??= "";
            info.profileEffectItem ??= "";
            info.profileIconItem ??= "";
            return info;
        }

        public static PlayerInfo ReadSaved(string json, out bool reset)
        {
            var info = new PlayerInfo { nameplateVersion = 0 };
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, info);
            reset = info.nameplateVersion == 0;
            return reset ? new PlayerInfo() : info;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
