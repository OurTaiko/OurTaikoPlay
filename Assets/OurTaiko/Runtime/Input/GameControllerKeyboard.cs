using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // A key going down or up on a keyboard read outside Unity's Keyboard device. Time is in the
    // source's own seconds (KeyboardSource.Now).
    public readonly struct KeyChange
    {
        public readonly Key Key;
        public readonly bool Down;
        public readonly double Time;
        public KeyChange(Key key, bool down, double time) { Key = key; Down = down; Time = time; }
    }

    // A keyboard InputManager reads instead of Unity's Keyboard while it is Active.
    public interface IKeyboardSource
    {
        bool Active { get; }
        // Seconds on the clock the changes are stamped with.
        double Now { get; }
        // Appends the changes since the last call, oldest first.
        void Drain(List<KeyChange> into);
    }

    // iOS: the hardware keyboard through the GameController framework (Plugins/iOS/OurTaikoKeyboard.mm).
    // iOS debounces UIKit's key presses, which Unity's Keyboard reads: a key struck again soon after
    // it was let go is dropped, so a drum on keyboard keys loses hits (issue #100). GCKeyboard sees
    // every change. Only a player on iOS has it; elsewhere Create returns null.
    public sealed class GameControllerKeyboard : IKeyboardSource
    {
        const int Batch = 256;
        readonly int[] codes = new int[Batch];
        readonly byte[] downs = new byte[Batch];
        readonly double[] times = new double[Batch];

        public static IKeyboardSource Create()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                bool connected = OurTaikoKeyboard_Start() != 0;
                Log.Info(InputLog.Tag, "GameController keyboard started; " + (connected ? "a keyboard is connected" : "no keyboard yet"));
                return new GameControllerKeyboard();
            }
            catch (Exception error)
            {
                Log.Warn(InputLog.Tag, "No GameController keyboard: " + error.Message);
            }
#endif
            return null;
        }

#if UNITY_IOS && !UNITY_EDITOR
        public bool Active => OurTaikoKeyboard_Connected() != 0;
        public double Now => OurTaikoKeyboard_Now();

        public void Drain(List<KeyChange> into)
        {
            int count;
            do
            {
                count = OurTaikoKeyboard_Drain(codes, downs, times, Batch);
                for (int i = 0; i < count; i++)
                    if (HidUsage.TryGetKey(codes[i], out var key)) into.Add(new KeyChange(key, downs[i] != 0, times[i]));
            }
            while (count == Batch);
        }

        [DllImport("__Internal")] static extern int OurTaikoKeyboard_Start();
        [DllImport("__Internal")] static extern int OurTaikoKeyboard_Connected();
        [DllImport("__Internal")] static extern double OurTaikoKeyboard_Now();
        [DllImport("__Internal")] static extern int OurTaikoKeyboard_Drain(int[] codes, byte[] downs, double[] times, int capacity);
#else
        public bool Active => false;
        public double Now => 0;
        public void Drain(List<KeyChange> into) { }
#endif
    }

    // USB HID keyboard usages (page 7), which GCKeyCode uses, to the Input System's keys.
    public static class HidUsage
    {
        static readonly Dictionary<int, Key> keys = Build();

        public static bool TryGetKey(int usage, out Key key) => keys.TryGetValue(usage, out key);

        static Dictionary<int, Key> Build()
        {
            var map = new Dictionary<int, Key>();
            for (int i = 0; i < 26; i++) map[0x04 + i] = Key.A + i;
            for (int i = 0; i < 9; i++) map[0x1E + i] = Key.Digit1 + i;
            map[0x27] = Key.Digit0;
            map[0x28] = Key.Enter; map[0x29] = Key.Escape; map[0x2A] = Key.Backspace; map[0x2B] = Key.Tab;
            map[0x2C] = Key.Space; map[0x2D] = Key.Minus; map[0x2E] = Key.Equals; map[0x2F] = Key.LeftBracket;
            map[0x30] = Key.RightBracket; map[0x31] = Key.Backslash; map[0x32] = Key.Backslash; map[0x33] = Key.Semicolon;
            map[0x34] = Key.Quote; map[0x35] = Key.Backquote; map[0x36] = Key.Comma; map[0x37] = Key.Period;
            map[0x38] = Key.Slash; map[0x39] = Key.CapsLock;
            for (int i = 0; i < 12; i++) map[0x3A + i] = Key.F1 + i;
            map[0x46] = Key.PrintScreen; map[0x47] = Key.ScrollLock; map[0x48] = Key.Pause; map[0x49] = Key.Insert;
            map[0x4A] = Key.Home; map[0x4B] = Key.PageUp; map[0x4C] = Key.Delete; map[0x4D] = Key.End;
            map[0x4E] = Key.PageDown; map[0x4F] = Key.RightArrow; map[0x50] = Key.LeftArrow; map[0x51] = Key.DownArrow;
            map[0x52] = Key.UpArrow; map[0x53] = Key.NumLock; map[0x54] = Key.NumpadDivide; map[0x55] = Key.NumpadMultiply;
            map[0x56] = Key.NumpadMinus; map[0x57] = Key.NumpadPlus; map[0x58] = Key.NumpadEnter;
            for (int i = 0; i < 9; i++) map[0x59 + i] = Key.Numpad1 + i;
            map[0x62] = Key.Numpad0; map[0x63] = Key.NumpadPeriod; map[0x64] = Key.OEM1; map[0x65] = Key.ContextMenu;
            map[0x67] = Key.NumpadEquals;
            for (int i = 0; i < 12; i++) map[0x68 + i] = Key.F13 + i;
            map[0xE0] = Key.LeftCtrl; map[0xE1] = Key.LeftShift; map[0xE2] = Key.LeftAlt; map[0xE3] = Key.LeftMeta;
            map[0xE4] = Key.RightCtrl; map[0xE5] = Key.RightShift; map[0xE6] = Key.RightAlt; map[0xE7] = Key.RightMeta;
            return map;
        }
    }
}
