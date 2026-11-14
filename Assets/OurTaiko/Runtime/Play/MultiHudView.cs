using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // マルチ演奏's scoreboard in the play scene: one card per player of the round along the top of
    // the stage, between the nameplate and the song title, each with the player's place by score,
    // name, score, combo and soul gauge, over the player's account picture when they have one
    // (MultiAvatars; darkened, so the numbers stay readable). The cards keep their order (the room's
    // places) while the rank numbers change. Built in code under the scene's 1920x1080 viewport when
    // a round is played.
    public sealed class MultiHudView
    {
        public const float CardWidth = 172, CardHeight = 204, Gap = 10, CentreX = 980, Top = 12;

        sealed class Card
        {
            public string UserId, AvatarUrl;
            public RectTransform Root;
            // The account picture behind the card, hidden until it has come.
            public GameObject Backdrop;
            public Image Photo;
            public Image Frame, Gauge, Mark;
            public TextMeshProUGUI Rank, Name, Score, Combo;
            public Image RankDisc;
            public int Shown = -1;
        }

        readonly MultiArt art;
        readonly RectTransform root;
        readonly List<Card> cards = new List<Card>();
        readonly Image waitingPlate;
        readonly TextMeshProUGUI waitingText;

        public int CardCount => cards.Count;
        public bool IsWaiting => waitingPlate.gameObject.activeSelf;
        // A card's texts, for tests: rank, name, score.
        public (string Rank, string Name, string Score) CardOf(string userId)
        {
            var card = cards.FirstOrDefault(c => c.UserId == userId);
            return card == null ? default : (card.Rank.text, card.Name.text, card.Score.text);
        }

        // The picture behind a player's card (null: none, or not here yet), for tests.
        public Sprite PhotoOf(string userId)
        {
            var card = cards.FirstOrDefault(c => c.UserId == userId);
            return card != null && card.Backdrop.activeSelf ? card.Photo.sprite : null;
        }

        public MultiHudView(RectTransform viewport)
        {
            art = MultiArt.Load();
            root = SkinUi.Rect("MultiHud", viewport, 1920, 1080);
            root.TopLeft(0, 0);
            waitingPlate = SkinUi.Image("Waiting", root, art.panel, 980, 110);
            waitingPlate.rectTransform.Center(960, 660);
            waitingText = SkinUi.Text("Text", waitingPlate.transform, 44);
            waitingText.rectTransform.anchorMin = waitingText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            waitingText.rectTransform.anchoredPosition = Vector2.zero;
            waitingText.text = MultiText.Get("play.waiting");
            waitingText.Squeeze(920);
            waitingPlate.gameObject.SetActive(false);
        }

        public void SetWaiting(bool waiting) => waitingPlate.gameObject.SetActive(waiting);

        // The round's players, in the room's order. Called when the room changes.
        public void SetPlayers(IEnumerable<Member> members, string myId)
        {
            var list = members.OrderBy(m => m.Slot).ToList();
            if (list.Select(m => m.UserId).SequenceEqual(cards.Select(c => c.UserId))) return;
            foreach (var card in cards) Object.Destroy(card.Root.gameObject);
            cards.Clear();
            float total = list.Count * CardWidth + (list.Count - 1) * Gap;
            for (int i = 0; i < list.Count; i++)
            {
                var member = list[i];
                var card = new Card { UserId = member.UserId, AvatarUrl = member.AvatarUrl };
                card.Root = SkinUi.Rect("Card" + i, root, CardWidth, CardHeight);
                card.Root.TopLeft(CentreX - total / 2 + i * (CardWidth + Gap), Top);
                var plate = SkinUi.Image("Plate", card.Root, art.panel, CardWidth, CardHeight);
                plate.rectTransform.Center(CardWidth / 2, CardHeight / 2);
                // The picture fills the card inside the plate's outline: cut to the plate's rounded
                // shape, covering the card's height, under a shade.
                var window = SkinUi.Image("Backdrop", card.Root, art.inset, CardWidth - 12, CardHeight - 12);
                window.rectTransform.Center(CardWidth / 2, CardHeight / 2);
                window.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                card.Backdrop = window.gameObject;
                card.Photo = SkinUi.Image("Photo", window.transform, null, CardHeight, CardHeight);
                card.Photo.rectTransform.Center((CardWidth - 12) / 2, (CardHeight - 12) / 2);
                var shade = SkinUi.Image("Shade", window.transform, null, CardWidth, CardHeight);
                shade.rectTransform.Center((CardWidth - 12) / 2, (CardHeight - 12) / 2);
                shade.color = new Color(0.05f, 0.03f, 0.12f, 0.5f);
                card.Backdrop.SetActive(false);
                var stripe = SkinUi.Image("Stripe", card.Root, art.bar, CardWidth - 28, 34);
                stripe.color = MultiArt.SlotColor(member.Slot);
                stripe.rectTransform.Center(CardWidth / 2 + 14, 34);
                card.Name = SkinUi.Text("Name", card.Root, 22);
                card.Name.rectTransform.Center(CardWidth / 2 + 14, 34);
                card.Name.text = MultiText.Name(member.Nickname);
                card.Name.Squeeze(CardWidth - 44);
                card.RankDisc = SkinUi.Image("RankDisc", card.Root, art.disc, 50, 50);
                card.RankDisc.rectTransform.Center(27, 34);
                card.Rank = SkinUi.Text("Rank", card.Root, 28);
                card.Rank.color = Color.black;
                card.Rank.UseUiFont();
                card.Rank.rectTransform.Center(27, 33);
                card.Score = SkinUi.Text("Score", card.Root, 36);
                card.Score.rectTransform.Center(CardWidth / 2, 92);
                card.Combo = SkinUi.Text("Combo", card.Root, 22);
                card.Combo.rectTransform.Center(CardWidth / 2, 134);
                var gaugeBack = SkinUi.Image("GaugeBack", card.Root, art.bar, CardWidth - 32, 18);
                gaugeBack.color = new Color(0, 0, 0, 0.7f);
                gaugeBack.rectTransform.Center(CardWidth / 2, 170);
                card.Gauge = SkinUi.Image("Gauge", card.Root, art.bar, CardWidth - 38, 12);
                card.Gauge.rectTransform.pivot = new Vector2(0, 0.5f);
                card.Gauge.rectTransform.anchoredPosition = new Vector2(19, -170);
                card.Mark = SkinUi.Image("Mark", card.Root, art.check, 40, 40);
                card.Mark.rectTransform.Center(CardWidth - 22, 134);
                card.Mark.enabled = false;
                card.Frame = SkinUi.Image("Own", card.Root, art.focus, CardWidth + 28, CardHeight + 28);
                card.Frame.rectTransform.Center(CardWidth / 2, CardHeight / 2);
                card.Frame.enabled = member.UserId == myId;
                cards.Add(card);
            }
        }

        // The cards from the room's statuses; `mine`, when given, is this player's own, newer than
        // what the room has sent back.
        public void Show(PlayStatuses statuses, string myId, PlayerStatus mine, bool cleared)
        {
            var byUser = new Dictionary<string, MemberStatus>();
            if (statuses != null) foreach (var s in statuses.Members) byUser[s.UserId] = s;
            var scores = new List<(Card Card, PlayerStatus Status, bool Connected, bool Finished)>();
            foreach (var card in cards)
            {
                byUser.TryGetValue(card.UserId, out var sent);
                bool own = card.UserId == myId && mine != null;
                scores.Add((card, own ? mine : sent?.Status, own || sent == null || sent.Connected, sent != null && sent.Finished));
            }
            foreach (var (card, status, connected, finished) in scores)
            {
                if (!card.Backdrop.activeSelf && MultiAvatars.GetSquare(card.AvatarUrl) is Sprite photo)
                {
                    card.Photo.sprite = photo;
                    card.Backdrop.SetActive(true);
                }
                int score = status?.Score ?? 0, combo = status?.Combo ?? 0, gauge = status?.Gauge ?? 0;
                // Equal scores share a place.
                int rank = 1 + scores.Count(other => (other.Status?.Score ?? 0) > score);
                card.Rank.text = rank.ToString(CultureInfo.InvariantCulture);
                card.RankDisc.color = RankColor(rank);
                if (card.Shown != score)
                {
                    card.Shown = score;
                    card.Score.text = score.ToString("N0", CultureInfo.InvariantCulture);
                    card.Score.Squeeze(CardWidth - 20);
                }
                card.Combo.text = combo >= 10 ? MultiText.Format("play.combo", combo) : "";
                float filled = Mathf.Clamp01(gauge / 10000f);
                card.Gauge.rectTransform.sizeDelta = new Vector2((CardWidth - 38) * filled, 12);
                bool clear = card.UserId == myId ? cleared : gauge >= 8000;
                card.Gauge.color = clear ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.36f, 0.3f);
                card.Gauge.enabled = filled > 0.01f;
                card.Mark.enabled = !connected || finished;
                if (card.Mark.enabled) card.Mark.sprite = connected ? art.check : art.offline;
            }
        }

        // Gold, silver, bronze, then plain.
        public static Color RankColor(int rank) => rank == 1 ? new Color(1f, 0.83f, 0.2f) : rank == 2 ? new Color(0.82f, 0.86f, 0.92f)
            : rank == 3 ? new Color(0.93f, 0.62f, 0.38f) : Color.white;
    }
}
