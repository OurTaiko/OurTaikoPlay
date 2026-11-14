using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OurTaiko
{
    // Multi.Room, マルチ演奏's screen (issue #38), reached from Entry's board. Out of a room it is a
    // menu: join a public room, make a private one, or enter a room's 5-digit number. In a room it
    // shows the members and what the room is doing, and offers what the player can do in that phase:
    // start (the host), pick a song (song select, or leave it to chance), pick a course of the song
    // drawn. The round itself is loaded by MultiDirector and played in Play.Single; afterwards this
    // screen shows the room's ranking of the round.
    //
    // The screen is drawn in code (MultiRoomScene.Build.cs): it is rebuilt whenever the room
    // changes, which is a few times a second at most. Ka (← →) moves between the controls, don
    // (Enter) presses the focused one, a tap or click presses a control directly, Back leaves: the
    // stamp list, the number entry, the room (asked twice), then the screen.
    public sealed partial class MultiRoomScene : MonoBehaviour
    {
        public RectTransform stage;
        public AudioSource bgm, sfx;

        public enum View { Menu, Code, Room, Result }

        // One thing the player can press.
        public sealed class Control
        {
            public string Key = "";
            public RectTransform Root;
            public Image Ring;
            public Action Press;
        }

        public View Shown { get; private set; }
        public IReadOnlyList<Control> Controls => controls;
        public Control Focused => focus >= 0 && focus < controls.Count ? controls[focus] : null;
        // Connecting to a room.
        public bool IsBusy { get; private set; }
        // The line under the menu: progress, or why the last room ended or could not be joined.
        public string Message { get; private set; } = "";
        // The room number typed so far.
        public string Code { get; private set; } = "";
        public bool StampsOpen { get; private set; }
        public bool HasLeft { get; private set; }

        const double LeaveConfirmSeconds = 3, StampSeconds = 3;

        SceneSwitcher switcher;
        MultiArt art;
        MultiDirector director;
        readonly List<Control> controls = new List<Control>();
        int focus;
        string focusKey = "";
        bool dirty = true, enteringCode, reviewing;
        double leaveAskedAt = double.NegativeInfinity;
        CancellationTokenSource cancel;
        // The stamp each member last sent, and until when it shows.
        readonly Dictionary<string, (int Stamp, double Until)> stamps = new Dictionary<string, (int, double)>();

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            art = MultiArt.Load();
            director = MultiDirector.Ensure();
            cancel = new CancellationTokenSource();
            // The mode has one player: whoever led the way here.
            switcher.SeatLeadAlone();
            switcher.PracticeMode = false;
            switcher.MultiPick = false;
            if (switcher.FromScene == SceneSwitcher.EntryScene || string.IsNullOrEmpty(switcher.FromScene)) director.BeginVisit();
            MultiSession.RoomChanged += OnRoomChanged;
            MultiSession.Closed += OnClosed;
            MultiSession.StampReceived += OnStamp;
            switcher.SceneChanging += OnSceneChanging;
            if (MultiSession.ClosedCode != null) { Message = MultiText.Describe(MultiSession.ClosedCode); MultiSession.ClosedCode = null; }
            BuildBackdrop();
            Rebuild();
        }

        void Start()
        {
            if (bgm != null && art.bgm != null)
            {
                bgm.SetAudioGroup(AudioGroup.Bgm);
                bgm.clip = art.bgm; bgm.loop = true;
                bgm.PlayAudio();
            }
            if (sfx != null) { sfx.SetAudioGroup(AudioGroup.Effects); sfx.PrepareAudioEffects(art.don, art.ka); }
        }

        void OnDestroy()
        {
            MultiSession.RoomChanged -= OnRoomChanged;
            MultiSession.Closed -= OnClosed;
            MultiSession.StampReceived -= OnStamp;
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
            cancel.Cancel(); cancel.Dispose();
        }

        void OnSceneChanging(string scene) { if (bgm != null) bgm.StopAudio(); }
        void OnRoomChanged(Room room) => dirty = true;

        void OnClosed(string code)
        {
            Message = code == "LEFT" ? "" : MultiText.Describe(code);
            MultiSession.ClosedCode = null;
            StampsOpen = false; enteringCode = false;
            stamps.Clear();
            dirty = true;
        }

        void OnStamp(StampSent stamp)
        {
            stamps[stamp.UserId] = (stamp.StampId, Time.realtimeSinceStartupAsDouble + StampSeconds);
            dirty = true;
        }

        // The round's ranking, when there is one the player has not seen (or asked to see again). A
        // room already picking its next song does not put it in the way.
        bool ResultWaiting
        {
            get
            {
                var result = MultiSession.LastResult;
                if (result == null) return false;
                if (reviewing) return true;
                if (result.Round <= MultiSession.ShownResultRound) return false;
                var room = MultiSession.Room;
                return room == null || room.Phase == Phase.Result || room.Phase == Phase.Lobby;
            }
        }

        View Wanted => ResultWaiting ? View.Result : MultiSession.InRoom ? View.Room : enteringCode ? View.Code : View.Menu;

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            foreach (var expired in stamps.Where(s => s.Value.Until <= now).Select(s => s.Key).ToList()) { stamps.Remove(expired); dirty = true; }
            if (Wanted != Shown) dirty = true;
            if (dirty) Rebuild();
            ShowClock();
            if (switcher.IsInputBlocked || HasLeft) return;
            if (Shown == View.Code) ReadDigits();
            if (InputManager.GetKeyDown(InputKey.Back)) Back();
            else if (InputManager.PlayerKeyDown(InputKey.LeftDon) || InputManager.PlayerKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Confirm();
            else if (InputManager.PlayerKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft) || InputManager.GetKeyDown(InputKey.MenuUp)) Move(-1);
            else if (InputManager.PlayerKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight) || InputManager.GetKeyDown(InputKey.MenuDown)) Move(1);
        }

        // ---------------------------------------------------------------- input

        public void Move(int delta)
        {
            if (controls.Count == 0 || HasLeft) return;
            focus = ((focus + delta) % controls.Count + controls.Count) % controls.Count;
            focusKey = controls[focus].Key;
            focusMoved = true;
            Play(art.ka);
            ShowFocus();
        }

        public void Confirm()
        {
            if (HasLeft || switcher.IsInputBlocked) return;
            var control = Focused;
            if (control == null) return;
            Play(art.don);
            control.Press?.Invoke();
        }

        // A tap or click: the control is focused and pressed.
        public void Tap(string key)
        {
            int index = controls.FindIndex(c => c.Key == key);
            if (index < 0 || HasLeft || switcher.IsInputBlocked) return;
            focus = index; focusKey = key;
            focusMoved = true;
            ShowFocus();
            Confirm();
        }

        public void Press(string key) => Tap(key);

        public void Back()
        {
            if (HasLeft || switcher.IsInputBlocked) return;
            Play(art.ka);
            if (StampsOpen) { StampsOpen = false; dirty = true; return; }
            switch (Shown)
            {
                case View.Result: DismissResult(); return;
                case View.Code: enteringCode = false; Code = ""; dirty = true; return;
                case View.Room:
                    // Leaving a room is asked twice: the round goes on without the player.
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - leaveAskedAt <= LeaveConfirmSeconds) LeaveRoom();
                    else { leaveAskedAt = now; director.Toast(MultiText.Get("room.leaveConfirm")); }
                    return;
                default:
                    if (IsBusy) return;
                    HasLeft = true;
                    MultiSession.Forget();
                    switcher.SwitchScene(SceneSwitcher.MenuScene);
                    return;
            }
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }

        // Digits and Backspace typed on a keyboard, beside the on-screen keys.
        void ReadDigits()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            for (int digit = 0; digit <= 9; digit++)
                if (keyboard[digit == 0 ? Key.Digit0 : Key.Digit1 + (digit - 1)].wasPressedThisFrame || keyboard[Key.Numpad0 + digit].wasPressedThisFrame) TypeDigit(digit);
            if (keyboard.backspaceKey.wasPressedThisFrame) DeleteDigit();
        }

        public void TypeDigit(int digit)
        {
            if (Code.Length >= 5 || IsBusy) return;
            Code += (char)('0' + digit);
            dirty = true;
        }

        public void DeleteDigit()
        {
            if (Code.Length == 0 || IsBusy) return;
            Code = Code.Substring(0, Code.Length - 1);
            dirty = true;
        }

        // ---------------------------------------------------------------- what the controls do

        public void QuickMatch() => Connect(() => MultiSession.QuickMatchAsync(cancel.Token));
        public void CreateRoom() => Connect(() => MultiSession.CreatePrivateAsync(cancel.Token));
        public void EnterCode() { enteringCode = true; Code = ""; Message = ""; dirty = true; }

        public void JoinTyped()
        {
            if (Code.Length != 5) { director.Toast(MultiText.Describe("BAD_ROOM_CODE")); return; }
            string code = Code;
            Connect(() => MultiSession.JoinAsync(code, cancel.Token));
        }

        async void Connect(Func<Task> join)
        {
            if (IsBusy || MultiSession.IsConnected) return;
            IsBusy = true;
            Message = MultiText.Get("menu.connecting");
            dirty = true;
            string failure = null;
            try { await join(); }
            catch (OperationCanceledException) { return; }
            catch (MultiException error) { failure = error.Code; }
            catch (Exception error)
            {
                Log.Warn("Multi", "Could not join a room: " + error.Message);
                failure = "CONNECTION_LOST";
            }
            if (this == null) return;
            IsBusy = false;
            Message = failure == null ? "" : MultiText.Describe(failure);
            if (failure == null) { enteringCode = false; Code = ""; }
            dirty = true;
        }

        public void LeaveRoom()
        {
            leaveAskedAt = double.NegativeInfinity;
            _ = MultiSession.LeaveAsync();
        }

        public void ChooseSong()
        {
            if (!director.OpenSongSelect()) return;
            HasLeft = true;
        }

        // おまかせ: any song, on the course the player last chose.
        public void LeaveToChance() => MultiSession.PickSong("", "", PreferredCourse());

        string PreferredCourse()
        {
            switch (switcher.LastDifficulty)
            {
                case (int)Difficulty.Easy: return "Easy";
                case (int)Difficulty.Normal: return "Normal";
                case (int)Difficulty.Hard: return "Hard";
                case (int)Difficulty.Ura: return "Edit";
                default: return "Oni";
            }
        }

        // A course of the drawn song: it is taken and the player is ready.
        public void TakeCourse(string course)
        {
            MultiSession.PickCourse(course);
            MultiSession.SetReady(true);
        }

        public void SendStamp(int id)
        {
            StampsOpen = false;
            MultiSession.SendStamp(id);
            dirty = true;
        }

        public void ShowLastResult() { reviewing = true; dirty = true; }

        public void DismissResult()
        {
            if (MultiSession.LastResult != null) MultiSession.ShownResultRound = Math.Max(MultiSession.ShownResultRound, MultiSession.LastResult.Round);
            reviewing = false;
            dirty = true;
        }
    }
}
