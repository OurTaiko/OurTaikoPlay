"""What each exported set is called in Resources/Stage.

The skin names a set differently in every folder (donbg a_01, bg_0, bg_fever_0, footer 0,
b_001_n, b_001_f, b_001_dai). The game calls a set by one name everywhere: common_<n> for
the sets any chart may be given, and the collab's own name for the sets a #SCENEPRESET asks for.
"""

import re

collabs = {
    "b_001": "miku",
    "b_002": "toho",
    "b_003": "gumi",
    "b_004": "ia",
    "b_005": "lovelive",
    "b_006": "i7id7",
    "b_007": "i7tri",
    "b_008": "i7rev",
    "b_009": "i7natsu",
    "b_010": "imas",
    "b_011": "imas_cg",
    "b_012": "imas_ml",
    "b_013": "imas_sidem",
    "b_014": "yokai",
    "b_015": "yokai_matsuri",
    "b_016": "yokai_hatsukoi",
    "b_017": "yokai_tokoroten",
    "b_018": "yokai_yougota",
    "b_019": "mario",
    "b_020": "a3",
}

# The skin's name for a common set in each folder, with its number as the group.
common = {
    "header": r"a_0(\d)",
    "scene": r"bg_(\d)",
    "clear": r"bg_fever_(\d)",
    "footer": r"footer(\d)",
    "crowd": r"(\d)",
    "renda": r"renda_(\d)",
    "dancers": r"dancer_(\d)",
}
# What the skin adds to a collab's name to say which part it is; the folder says that here.
part = {"scene": "_n", "clear": "_f", "footer": "_dai", "dancers": "_d"}
# What follows the set's name and is kept: 2P's header, a dancer's place in the troupe.
kept = {"header": r"(_2p)?", "dancers": r"(_\d)?"}


def named(folder, stem):
    tail = kept.get(folder, "()")
    m = folder in common and re.fullmatch(common[folder] + tail, stem)
    if m:
        # The skin counts its headers from 1.
        n = int(m.group(1)) - (1 if folder == "header" else 0)
        return f"common_{n}{m.group(2) or ''}"
    m = re.fullmatch(r"(b_\d{3})(?:" + re.escape(part.get(folder, "")) + ")?" + tail, stem)
    if m:
        return collabs[m.group(1)] + (m.group(2) or "")
    return stem
