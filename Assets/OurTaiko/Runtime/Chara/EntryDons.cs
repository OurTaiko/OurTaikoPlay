using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // Entry's 3D Dons (YataiDON EntryPlayer's chara): each seat's beside its plate once it joins,
    // jumping up from below the screen. YataiDON's side-select Don (skin "entry", the middle of the
    // screen) is left out: this Entry has the arcade credit rows there instead of a side select.
    // Nijiiro Scripts/entry/player.lua: DON_CX 220 / 1700, DON_CY 780, anchor -5, +130;
    // animation.json entry 2: up 615 px by 317 ms, back down to rest at 500 ms.
    public sealed class EntryDons
    {
        static readonly Vector2 Seat1P = new Vector2(215, 910), Seat2P = new Vector2(1695, 910);
        const double RiseMs = 317, SettleMs = 500;
        const float Rise = 615, Rest = 510;

        readonly EntryView view;
        readonly DonView[] seats = new DonView[2];

        public EntryDons(EntryView view) => this.view = view;

        // How far below its rest a joining Don is, ms after the join.
        public static float Drop(double ms)
        {
            if (ms <= 0) return Rest;
            if (ms < RiseMs)
            {
                double u = ms / RiseMs;
                return (float)(Rest - Rise * u * u);
            }
            if (ms < SettleMs)
            {
                double u = (ms - RiseMs) / (SettleMs - RiseMs);
                double e = 1 - (1 - u) * (1 - u);
                return (float)(Rest - Rise + (Rise - Rest) * e);
            }
            return 0;
        }

        public void Show(double? joined1P, double? joined2P, double now)
        {
            ShowSeat(0, joined1P, now);
            ShowSeat(1, joined2P, now);
        }

        void ShowSeat(int seat, double? joinedAt, double now)
        {
            if (!joinedAt.HasValue) return;
            var spot = seat == 0 ? Seat1P : Seat2P;
            if (seats[seat] == null)
            {
                Component plate = seat == 0 ? view.nameplate : view.nameplate2P;
                if (plate == null) return;
                seats[seat] = DonStage.Place(plate, seat == 0 ? "Don1P" : "Don2P", spot, seat, seat == 1, DonAnim.EntryIn);
                if (seats[seat] == null) return;
            }
            seats[seat].MoveTo(spot.x, spot.y + Drop(now - joinedAt.Value));
        }
    }
}
