using System;
using System.Globalization;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Multi.Room's drawing: every view is laid out here on the 1920x1080 stage, in skin pixels from
    // the top left, from the MultiArt sprites and the one UI font. Rebuild draws the view the state
    // asks for and lists its controls; the focus stays on the control it was on when that control
    // is still there.
    public sealed partial class MultiRoomScene
    {
        static readonly Color Ink = new Color(0.1f, 0.08f, 0.16f);
        static readonly Color Orange = new Color(1f, 0.62f, 0.2f), Teal = new Color(0.3f, 0.82f, 0.74f), Violet = new Color(0.74f, 0.6f, 0.96f),
            Slate = new Color(0.72f, 0.78f, 0.88f), Rose = new Color(1f, 0.52f, 0.5f), Paper = new Color(1f, 0.98f, 0.93f), Dim = new Color(0.55f, 0.55f, 0.6f);

        RectTransform content;
        string focusContext = "";
        bool focusMoved;
        TextMeshProUGUI clockText;
        Image clockIcon;

        // The texts on screen, for tests.
        public string AllText => content == null ? "" : string.Join("\n", content.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text));

        void BuildBackdrop()
        {
            var background = SkinUi.Image("Background", stage, art.background, 1920, 1080);
            background.rectTransform.Center(960, 540);
            var mark = SkinUi.Image("Mark", stage, art.mark, 104, 104);
            mark.rectTransform.Center(92, 76);
            Label(stage, MultiText.Get("title"), 58, 160, 74, 0, false, -1);
            Label(stage, MultiText.Get("hint"), 28, 960, 1048);
        }

        void Rebuild()
        {
            dirty = false;
            Shown = Wanted;
            if (content != null) Destroy(content.gameObject);
            content = SkinUi.Rect("Content", stage, 1920, 1080);
            content.TopLeft(0, 0);
            controls.Clear();
            clockText = null; clockIcon = null;
            string preferred = "";
            switch (Shown)
            {
                case View.Menu: BuildMenu(); break;
                case View.Code: BuildCode(); preferred = "key:ok"; break;
                case View.Result: BuildResult(MultiSession.LastResult); break;
                default: preferred = BuildRoom(MultiSession.Room); break;
            }
            // The focus stays where the player put it; until they move it in this view and phase, it
            // is on the control that matters most there (the host's start plate once it appears).
            string context = Shown + "/" + (MultiSession.Room?.Phase.ToString() ?? "") + "/" + StampsOpen;
            if (context != focusContext) { focusContext = context; focusMoved = false; }
            int kept = controls.FindIndex(c => c.Key == focusKey), wanted = controls.FindIndex(c => c.Key == preferred);
            focus = Math.Max(0, focusMoved && kept >= 0 ? kept : wanted >= 0 ? wanted : kept);
            focusKey = Focused?.Key ?? "";
            ShowFocus();
            ShowClock();
        }

        void ShowFocus()
        {
            for (int i = 0; i < controls.Count; i++)
                if (controls[i].Ring != null) controls[i].Ring.enabled = i == focus;
        }

        void ShowClock()
        {
            if (clockText == null) return;
            long left = MultiSession.RemainingMs;
            bool shown = left >= 0;
            clockText.enabled = clockIcon.enabled = shown;
            if (shown) clockText.text = ((left + 999) / 1000).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- pieces

        // align: -1 left of x, 0 centred on x, 1 right of x.
        TextMeshProUGUI Label(Transform parent, string text, float size, float x, float y, float maxWidth = 0, bool dark = false, int align = 0)
        {
            var label = SkinUi.Text("Label", parent, size);
            if (dark) { label.color = Ink; label.UseUiFont(); }
            label.alignment = align < 0 ? TextAlignmentOptions.Left : align > 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            label.rectTransform.pivot = new Vector2(align < 0 ? 0 : align > 0 ? 1 : 0.5f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(x, -y);
            label.text = text ?? "";
            if (maxWidth > 0) label.Squeeze(maxWidth);
            return label;
        }

        Image Picture(Transform parent, Sprite sprite, float width, float height, float x, float y, Color? color = null)
        {
            var image = SkinUi.Image(sprite != null ? sprite.name : "Image", parent, sprite, width, height);
            image.rectTransform.Center(x, y);
            if (color.HasValue) image.color = color.Value;
            return image;
        }

        // A plate the player can press. Its children are placed from its top left corner.
        RectTransform Button(string key, Transform parent, float width, float height, float x, float y, Color color, Action press, bool enabled = true)
        {
            var root = SkinUi.Rect(key, parent, width, height);
            root.Center(x, y);
            var plate = Picture(root, art.plate, width, height, width / 2, height / 2, enabled ? color : Dim);
            var ring = Picture(root, art.focus, width + 34, height + 34, width / 2, height / 2 - 2);
            ring.enabled = false;
            if (!enabled) return root;
            plate.raycastTarget = true;
            plate.gameObject.AddComponent<PointerRelay>().Clicked = () => Tap(key);
            controls.Add(new Control { Key = key, Root = root, Ring = ring, Press = press });
            return root;
        }

        RectTransform TextButton(string key, Transform parent, float width, float height, float x, float y, Color color, string text, float size, Action press, bool enabled = true)
        {
            var root = Button(key, parent, width, height, x, y, color, press, enabled);
            Label(root, text, size, width / 2, height / 2 - 3, width - 36, true);
            return root;
        }

        // A member's disc: their colour and the first letter of their name.
        void Avatar(Transform parent, Member member, float size, float x, float y) => Avatar(parent, member.Slot, member.Nickname, size, x, y);

        // With an address, the player's account picture once it has come (MultiAvatars); until
        // then, and without one, the letter.
        void Avatar(Transform parent, int slot, string nickname, float size, float x, float y, string pictureUrl = null)
        {
            var picture = MultiAvatars.Get(pictureUrl);
            Picture(parent, art.disc, size, size, x, y, picture != null ? Color.white : MultiArt.SlotColor(slot));
            if (picture != null) { Picture(parent, picture, size * 0.84f, size * 0.84f, x, y); return; }
            string name = MultiText.Name(nickname);
            Label(parent, StringInfo.GetNextTextElement(name).ToUpperInvariant(), size * 0.5f, x, y - size * 0.03f);
        }

        // A course as the skin's own icon with its stars beside it; a course the skin has no icon
        // for keeps its name.
        void CourseIcon(Transform parent, string course, int level, float height, float x, float y)
        {
            var icon = art.CourseIcon(course);
            if (icon == null) { CoursePill(parent, course, level, 210, Math.Min(54, height), x, y); return; }
            float width = height * icon.rect.width / icon.rect.height;
            Picture(parent, icon, width, height, x - 34, y);
            if (level > 0) Label(parent, "★" + level, height * 0.44f, x + width / 2 - 26, y - 2, 0, false, -1);
        }

        // The result screen's own labels, each with its count, one after another from x: 良, 可 and
        // 不可 on the first line, 最大コンボ数 and 連打数 on the second.
        void Counts(Transform parent, float x, float y, float height, float gap, params (Sprite Label, int Count)[] counts)
        {
            foreach (var (label, count) in counts)
            {
                if (label != null)
                {
                    float width = height * label.rect.width / label.rect.height;
                    Picture(parent, label, width, height, x + width / 2, y);
                    x += width + 6;
                }
                var number = Label(parent, count.ToString(CultureInfo.InvariantCulture), height * 0.84f, x, y - 2, 0, false, -1);
                x += number.preferredWidth + gap;
            }
        }

        // A course's name and stars on its colour.
        void CoursePill(Transform parent, string course, int level, float width, float height, float x, float y)
        {
            Picture(parent, art.plate, width, height, x, y, MultiArt.CourseColor(course));
            string text = MultiText.Course(course) + (level > 0 ? " ★" + level : "");
            Label(parent, text, height * 0.46f, x, y - 3, width - 30);
        }

        static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- menu

        void BuildMenu()
        {
            MenuBoard("quick", 380, Orange, art.open, "menu.quick", QuickMatch);
            MenuBoard("create", 960, Teal, art.locked, "menu.create", CreateRoom);
            MenuBoard("join", 1540, Violet, art.mark, "menu.join", EnterCode);
            TextButton("back", content, 300, 92, 210, 950, Slate, L10n.Get("Common", "back"), 38, Back, !IsBusy);
            if (Message.Length > 0)
            {
                Picture(content, art.panel, 1500, 96, 960, 800);
                Label(content, Message, 36, 960, 798, 1440);
            }
        }

        void MenuBoard(string key, float x, Color color, Sprite icon, string text, Action press)
        {
            var root = Button(key, content, 520, 340, x, 500, color, press, !IsBusy);
            Picture(root, art.inset, 464, 132, 260, 258, new Color(1, 1, 1, 0.5f));
            Picture(root, icon, 100, 100, 260, 70);
            Label(root, MultiText.Get(text + ".title"), 44, 260, 148, 470, true);
            Label(root, MultiText.Get(text + ".info0"), 26, 260, 236, 440, true);
            Label(root, MultiText.Get(text + ".info1"), 26, 260, 278, 440, true);
        }

        // ---------------------------------------------------------------- room number

        void BuildCode()
        {
            Picture(content, art.panel, 760, 780, 960, 530);
            Label(content, MultiText.Get("code.heading"), 44, 960, 196, 700);
            for (int i = 0; i < 5; i++)
            {
                float x = 960 + (i - 2) * 120;
                Picture(content, art.plate, 108, 128, x, 306, Paper);
                Label(content, i < Code.Length ? Code[i].ToString() : "", 72, x, 300, 0, true);
            }
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "<", "0", "ok" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                float x = 960 + (i % 3 - 1) * 186, y = 452 + i / 3 * 110;
                if (key == "<") TextButton("key:delete", content, 170, 96, x, y, Slate, "<", 48, DeleteDigit, !IsBusy);
                else if (key == "ok") TextButton("key:ok", content, 170, 96, x, y, Orange, L10n.Get("Common", "ok"), 40, JoinTyped, !IsBusy);
                else
                {
                    int digit = key[0] - '0';
                    TextButton("key:" + key, content, 170, 96, x, y, Paper, key, 52, () => TypeDigit(digit), !IsBusy);
                }
            }
            TextButton("back", content, 300, 92, 210, 950, Slate, L10n.Get("Common", "back"), 38, Back, !IsBusy);
            if (Message.Length > 0) Label(content, Message, 34, 960, 968, 1100);
        }

        // ---------------------------------------------------------------- room

        // Returns the control the focus starts on.
        string BuildRoom(Room room)
        {
            var me = MultiSession.Me;
            bool host = MultiSession.IsHost;
            Picture(content, art.panel, 1800, 116, 960, 198);
            Picture(content, room.Public ? art.open : art.locked, 64, 64, 112, 196);
            Label(content, MultiText.Format("room.number", room.Code), 54, 160, 194, 0, false, -1);
            Label(content, MultiText.Get(room.Public ? "room.public" : "room.private"), 28, 560, 200, 0, false, -1);
            Label(content, MultiDirector.PhaseText(room), 44, 1010, 194, 560);
            clockIcon = Picture(content, art.clock, 58, 58, 1650, 196);
            clockText = Label(content, "", 56, 1700, 194, 0, false, -1);

            int slots = Math.Max(1, Math.Max(room.MaxMembers, room.Members.Count));
            float width = Math.Min(336, (1800f - (slots - 1) * 16) / slots);
            for (int i = 0; i < slots; i++)
            {
                float x = 960 + (i - (slots - 1) / 2f) * (width + 16);
                var member = i < room.Members.Count ? room.Members.OrderBy(m => m.Slot).ElementAt(i) : null;
                MemberCard(room, member, me, host, x, 432, width);
            }

            string preferred = StampsOpen ? "stamp:0" : BuildPhase(room, me, host);
            if (StampsOpen) { BuildStamps(); return preferred; }
            TextButton("stamps", content, 300, 92, 210, 950, Violet, MultiText.Get("room.stamps"), 36, () => { StampsOpen = true; dirty = true; });
            if (MultiSession.LastResult != null && room.Phase == Phase.Lobby)
                TextButton("lastResult", content, 340, 92, 560, 950, Slate, MultiText.Get("room.lastResult"), 34, ShowLastResult);
            TextButton("leave", content, 300, 92, 1710, 950, Rose, MultiText.Get("room.leave"), 36, LeaveRoom);
            return preferred;
        }

        void MemberCard(Room room, Member member, Member me, bool host, float x, float y, float width)
        {
            const float height = 264;
            var root = SkinUi.Rect(member != null ? "Member" + member.Slot : "Empty", content, width, height);
            root.Center(x, y);
            float mid = width / 2;
            if (member == null)
            {
                Picture(root, art.panel, width, height, mid, height / 2, new Color(1, 1, 1, 0.45f));
                Label(root, MultiText.Get("room.empty"), 30, mid, height / 2, width - 40).alpha = 0.6f;
                return;
            }
            bool mine = me != null && member.UserId == me.UserId;
            Picture(root, art.plate, width, height, mid, height / 2, member.Connected ? Paper : Dim);
            if (mine) Picture(root, art.inset, width - 28, 60, mid, 154, new Color(1f, 0.9f, 0.45f, 0.9f));
            Avatar(root, member, 112, mid, 70);
            if (member.UserId == room.HostId) Picture(root, art.crownHost, 60, 54, 42, 34);
            Label(root, MultiText.Name(member.Nickname), 32, mid, 152, width - 44, true);
            MemberStatus(root, room, member, mid, 214, width);
            if (host && !mine && room.Phase == Phase.Lobby && !StampsOpen)
            {
                string id = member.UserId;
                var kick = Button("kick:" + id, root, 56, 56, width - 36, 36, Rose, () => MultiSession.Kick(id));
                Label(kick, "X", 30, 28, 26, 0, true);
            }
            if (stamps.TryGetValue(member.UserId, out var sent) && sent.Stamp >= 0 && sent.Stamp < art.stamps.Length)
            {
                var bubble = SkinUi.Rect("Stamp", root, 210, 220);
                bubble.Center(mid, 50);
                Picture(bubble, art.bubble, 210, 220, 105, 110);
                Picture(bubble, art.stamps[sent.Stamp], 118, 118, 105, 70);
                Label(bubble, MultiText.Stamp(sent.Stamp), 22, 105, 148, 176, true);
            }
        }

        // The line at the foot of a member's card: what they have done in this phase.
        void MemberStatus(Transform root, Room room, Member member, float x, float y, float width)
        {
            if (!member.Connected) { Picture(root, art.offline, 44, 44, x - 70, y); Label(root, MultiText.Get("member.offline"), 26, x + 24, y - 2, width - 130, true); return; }
            switch (room.Phase)
            {
                case Phase.SongSelect:
                    Picture(root, member.Picked ? art.check : art.wait, 46, 46, x, y);
                    break;
                case Phase.SongDraw:
                case Phase.Course:
                    CoursePill(root, member.Course, LevelOf(room, member.Course), Math.Min(width - 90, 220), 50, member.Ready ? x - 28 : x, y);
                    if (member.Ready) Picture(root, art.check, 46, 46, x + Math.Min(width - 90, 220) / 2 + 4, y);
                    break;
                case Phase.Loading:
                    if (!member.Playing) Label(root, MultiText.Get("member.sittingOut"), 26, x, y - 2, width - 40, true);
                    else Picture(root, member.Loaded ? art.check : art.wait, 46, 46, x, y);
                    break;
                case Phase.Playing:
                    if (!member.Playing) { Label(root, MultiText.Get("member.sittingOut"), 26, x, y - 2, width - 40, true); break; }
                    var status = MultiSession.Statuses?.Members.FirstOrDefault(s => s.UserId == member.UserId);
                    Label(root, Number(status?.Status?.Score ?? 0), 32, member.Finished ? x - 26 : x, y - 2, width - 110, true);
                    if (member.Finished) Picture(root, art.check, 42, 42, x + width / 2 - 44, y);
                    break;
            }
        }

        static int LevelOf(Room room, string course) => room.Song?.Courses.FirstOrDefault(c => c.Course_ == course)?.Level ?? 0;

        // What the player can do in the room's phase. Returns the control the focus starts on.
        string BuildPhase(Room room, Member me, bool host)
        {
            switch (room.Phase)
            {
                case Phase.Lobby:
                    int present = room.Members.Count(m => m.Connected), needed = Math.Max(1, room.MinPlayers);
                    if (host)
                    {
                        if (!room.Public && !StampsOpen)
                        {
                            var open = Button("open", content, 420, 150, 480, 716, Teal, MultiSession.OpenRoom);
                            Label(open, MultiText.Get("room.open"), 36, 210, 54, 380, true);
                            Label(open, MultiText.Get("room.openInfo"), 22, 210, 104, 380, true);
                        }
                        if (!StampsOpen)
                        {
                            var start = Button("start", content, 460, 150, 960, 716, Orange, MultiSession.LockMembers, present >= needed);
                            Label(start, MultiText.Get("room.start"), 48, 230, 54, 420, true);
                            Label(start, present >= needed ? MultiText.Format("room.players", present) : MultiText.Format("room.needMore", needed - present), 24, 230, 106, 420, true);
                        }
                        return "start";
                    }
                    Picture(content, art.panel, 1100, 110, 960, 716);
                    Label(content, MultiText.Get(room.Public && present >= needed ? "room.startingSoon" : "room.waitingHost"), 38, 960, 714, 1040);
                    return "stamps";
                case Phase.SongSelect:
                    if (me != null && !me.Picked && !StampsOpen)
                    {
                        var choose = Button("choose", content, 480, 160, 690, 716, Orange, ChooseSong);
                        Label(choose, MultiText.Get("pick.choose"), 46, 240, 56, 440, true);
                        Label(choose, MultiText.Get("pick.chooseInfo"), 23, 240, 112, 440, true);
                        var chance = Button("chance", content, 480, 160, 1230, 716, Teal, LeaveToChance);
                        Label(chance, MultiText.Get("pick.chance"), 46, 240, 56, 440, true);
                        Label(chance, MultiText.Get("pick.chanceInfo"), 23, 240, 112, 440, true);
                        return "choose";
                    }
                    Picture(content, art.panel, 1100, 110, 960, 716);
                    Label(content, MultiText.Get("pick.done"), 38, 960, 714, 1040);
                    return "stamps";
                case Phase.SongDraw:
                    SongPanel(room, 716);
                    return "stamps";
                case Phase.Course:
                    SongPanel(room, 650, 150);
                    if (room.Song == null || me == null) return "stamps";
                    int count = room.Song.Courses.Count;
                    for (int i = 0; i < count && !StampsOpen; i++)
                    {
                        var course = room.Song.Courses[i];
                        string name = course.Course_;
                        float x = 960 + (i - (count - 1) / 2f) * 290;
                        bool taken = me.Course == name;
                        var plate = Button("course:" + name, content, 270, 110, x, 830, MultiArt.CourseColor(name), () => TakeCourse(name), !me.Ready);
                        // The course the player has now is lit; once ready, it is ticked.
                        if (taken) Picture(plate, art.inset, 238, 78, 135, 52, new Color(1, 1, 1, 0.3f));
                        Label(plate, MultiText.Course(name) + " ★" + course.Level, 34, 135, 50, 226);
                        if (taken && me.Ready) Picture(plate, art.check, 48, 48, 248, 14);
                    }
                    return "course:" + me.Course;
                case Phase.Loading:
                    Picture(content, art.panel, 1100, 110, 960, 716);
                    Label(content, MultiText.Get(me != null && me.Playing ? "phase.loading" : "play.sittingOut"), 38, 960, 714, 1040);
                    return "stamps";
                case Phase.Playing:
                    Picture(content, art.panel, 1100, 110, 960, 716);
                    Label(content, MultiText.Get("play.sittingOut"), 38, 960, 714, 1040);
                    return "stamps";
                default:
                    Picture(content, art.panel, 1100, 110, 960, 716);
                    Label(content, MultiText.Get("result.backSoon"), 38, 960, 714, 1040);
                    return "stamps";
            }
        }

        // The drawn song: its title, subtitle and whose pick it was.
        void SongPanel(Room room, float y, float height = 230)
        {
            if (room.Song == null) return;
            Picture(content, art.panel, 1300, height, 960, y);
            bool tall = height > 200;
            Label(content, room.Song.Title, tall ? 52 : 44, 960, y - (tall ? 48 : 22), 1220);
            if (tall && room.Song.Subtitle.Length > 0) Label(content, room.Song.Subtitle, 28, 960, y + 8, 1220);
            var picker = MultiSession.Find(room.SongPickedBy);
            string by = picker != null ? MultiText.Format("draw.pickedBy", MultiText.Name(picker.Nickname)) : MultiText.Get("draw.chance");
            Label(content, by, 28, 960, y + (tall ? 64 : 34), 1220).color = new Color(1f, 0.9f, 0.5f);
        }

        void BuildStamps()
        {
            Picture(content, art.panel, 1560, 500, 960, 790);
            int count = Math.Min(MultiSession.StampCount, art.stamps.Length);
            for (int i = 0; i < count; i++)
            {
                int id = i;
                float x = 960 + (i % 8 - 3.5f) * 184, y = 656 + i / 8 * 196;
                var stamp = Button("stamp:" + i, content, 172, 190, x, y, Paper, () => SendStamp(id));
                Picture(stamp, art.stamps[i], 112, 112, 86, 70);
                Label(stamp, MultiText.Stamp(i), 21, 86, 148, 150, true);
            }
            TextButton("closeStamps", content, 300, 70, 960, 1000, Slate, L10n.Get("Common", "close"), 34, () => { StampsOpen = false; dirty = true; });
        }

        // ---------------------------------------------------------------- result

        void BuildResult(Multi.Proto.PlayResult result)
        {
            Picture(content, null, 1920, 1080, 960, 540, new Color(0, 0, 0, 0.5f));
            Picture(content, art.panel, 1700, 890, 960, 540);
            Label(content, MultiText.Get("result.heading"), 54, 960, 150, 700);
            Label(content, result.Title, 34, 960, 214, 1100).color = new Color(1f, 0.9f, 0.5f);
            Label(content, MultiText.Format("result.team", Number(result.TeamScore)), 34, 1760, 150, 480, false, 1);
            int count = Math.Max(1, result.Players.Count);
            float rowHeight = Math.Min(118, 590f / count);
            for (int i = 0; i < result.Players.Count; i++) ResultRow(result.Players[i], 290 + i * rowHeight + rowHeight / 2 - 30, rowHeight);
            TextButton("ok", content, 320, 92, 960, 930, Orange, L10n.Get("Common", "ok"), 40, DismissResult);
        }

        void ResultRow(PlayerResult player, float y, float rowHeight)
        {
            bool mine = player.UserId == MultiSession.MyUserId;
            float bar = rowHeight - 12;
            Picture(content, art.inset, 1620, bar, 960, y, mine ? new Color(1f, 0.9f, 0.45f, 0.32f) : new Color(1, 1, 1, 0.1f));
            float disc = Math.Min(74, bar - 6);
            Picture(content, art.disc, disc, disc, 200, y, MultiHudView.RankColor(player.Rank));
            Label(content, player.Rank.ToString(CultureInfo.InvariantCulture), disc * 0.52f, 200, y - 2, 0, true);
            Avatar(content, player.Slot, player.Nickname, disc, 290, y, player.AvatarUrl);
            Label(content, MultiText.Name(player.Nickname), 34, 344, y - 2, 300, false, -1);
            CourseIcon(content, player.Course, player.Level, Math.Min(72, bar - 8), 770, y);
            var status = player.Status ?? new PlayerStatus();
            Label(content, Number(status.Score), 46, 1120, y - 2, 230, false, 1);
            bool twoLines = bar >= 84;
            Counts(content, 1146, twoLines ? y - 20 : y, 36, 16, (art.judgeGood, status.Good), (art.judgeOk, status.Ok), (art.judgeBad, status.Bad));
            if (twoLines) Counts(content, 1146, y + 22, 30, 18, (art.labelMaxCombo, status.MaxCombo), (art.labelDrumroll, status.Drumroll));
            if (player.Disconnected) Label(content, MultiText.Get("result.disconnected"), 28, 1530, y - 2, 124).color = Rose;
            else
            {
                var crown = player.AllGood ? art.crownDonderful : player.FullCombo ? art.crownFullCombo : player.Cleared ? art.crownClear : null;
                if (crown != null) Picture(content, crown, 68, 60, 1530, y);
                else Label(content, MultiText.Get("result.failed"), 24, 1530, y - 2, 124).alpha = 0.7f;
            }
            if (player.Award == Award.Mvp || player.Award == Award.SuperStar)
            {
                bool super = player.Award == Award.SuperStar;
                Picture(content, art.ribbon, 172, 58, 1688, y, super ? new Color(1f, 0.83f, 0.2f) : new Color(1f, 0.55f, 0.5f));
                Label(content, MultiText.Get(super ? "result.superStar" : "result.mvp"), 22, 1688, y - 6, 100, true);
            }
        }
    }
}
