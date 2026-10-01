using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace OurTaiko
{
    // Logical inputs. Scenes ask for these instead of reading physical keys.
    public enum InputKey
    {
        LeftDon, RightDon, LeftKa, RightKa,
        Confirm, Back, Pause, Restart, ToggleAuto, NextSong, SongSelect, MenuLeft, MenuRight,
    }

    public readonly struct InputPress
    {
        public readonly InputKey Key;
        // Input System time (seconds, same base as InputState.currentTime).
        public readonly double Time;
        public InputPress(InputKey key, double time) { Key = key; Time = time; }
    }

    public static class InputKeyExtensions
    {
        public static bool IsDrum(this InputKey key) => key <= InputKey.RightKa;
        public static bool IsKa(this InputKey key) => key == InputKey.LeftKa || key == InputKey.RightKa;
        public static bool IsRight(this InputKey key) => key == InputKey.RightDon || key == InputKey.RightKa;
    }

    // Single entry point for game input, after MajdataPlay's IO/InputManager:
    // raw keyboard events are collected as they arrive, then published once per frame
    // (before any scene Update) as an ordered list of presses plus per-key flags.
    public static class InputManager
    {
        static readonly int KeyCount = Enum.GetValues(typeof(InputKey)).Length;
        static readonly Key[][] bindings = new Key[KeyCount][];
        static readonly Dictionary<Key, InputKey> reverse = new Dictionary<Key, InputKey>();
        static readonly List<InputPress> pending = new List<InputPress>();
        static readonly List<InputPress> frame = new List<InputPress>();
        static readonly bool[] pressedThisFrame = new bool[KeyCount];
        static readonly bool[] held = new bool[KeyCount];
        static bool installed;

        // Presses of this frame in the order they happened.
        public static IReadOnlyList<InputPress> PressesThisFrame => frame;
        public static bool GetKeyDown(InputKey key) => pressedThisFrame[(int)key];
        public static bool GetKey(InputKey key) => held[(int)key];

        static InputManager() => ResetBindings();

        public static void ResetBindings()
        {
            SetBinding(InputKey.LeftDon, Key.F);
            SetBinding(InputKey.RightDon, Key.J);
            SetBinding(InputKey.LeftKa, Key.D);
            SetBinding(InputKey.RightKa, Key.K);
            SetBinding(InputKey.Confirm, Key.Enter, Key.NumpadEnter);
            SetBinding(InputKey.Back, Key.Escape);
            SetBinding(InputKey.Pause, Key.Space);
            SetBinding(InputKey.Restart, Key.F1);
            SetBinding(InputKey.ToggleAuto, Key.A);
            SetBinding(InputKey.NextSong, Key.Tab);
            SetBinding(InputKey.SongSelect, Key.S);
            SetBinding(InputKey.MenuLeft, Key.LeftArrow);
            SetBinding(InputKey.MenuRight, Key.RightArrow);
        }

        // A physical key drives at most one logical key; rebinding takes it from the old owner.
        public static void SetBinding(InputKey key, params Key[] keys)
        {
            foreach (var old in bindings[(int)key] ?? Array.Empty<Key>()) reverse.Remove(old);
            foreach (var physical in keys)
            {
                if (reverse.TryGetValue(physical, out var owner))
                    bindings[(int)owner] = Array.FindAll(bindings[(int)owner], k => k != physical);
                reverse[physical] = key;
            }
            bindings[(int)key] = (Key[])keys.Clone();
        }
        public static IReadOnlyList<Key> GetBinding(InputKey key) => bindings[(int)key];

        // Non-keyboard sources (on-screen drum pads) feed the same stream; published next frame.
        public static void Press(InputKey key) => pending.Add(new InputPress(key, InputState.currentTime));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pending.Clear(); frame.Clear();
            Array.Clear(pressedThisFrame, 0, KeyCount); Array.Clear(held, 0, KeyCount);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!installed) { InputSystem.onEvent += OnInputEvent; installed = true; }
            if (UnityEngine.Object.FindFirstObjectByType<InputManagerUpdater>() != null) return;
            var updater = new GameObject(nameof(InputManagerUpdater)) { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(updater);
            updater.AddComponent<InputManagerUpdater>();
        }

        // Runs while the Input System processes events, before the key state is written,
        // so `isPressed` still holds the previous state and the call order is the press order.
        static void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!(device is Keyboard keyboard)) return;
            if (InputState.currentUpdateType == InputUpdateType.Editor) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            foreach (var pair in reverse)
            {
                KeyControl control = keyboard[pair.Key];
                if (control == null || control.isPressed) continue;
                if (control.ReadValueFromEvent(eventPtr, out float value) && control.IsValueConsideredPressed(value))
                    pending.Add(new InputPress(pair.Value, eventPtr.time));
            }
        }

        // Called once per frame by InputManagerUpdater, ahead of every scene script.
        internal static void OnPreUpdate()
        {
            frame.Clear(); frame.AddRange(pending); pending.Clear();
            Array.Clear(pressedThisFrame, 0, KeyCount);
            foreach (var press in frame) pressedThisFrame[(int)press.Key] = true;
            var keyboard = Keyboard.current;
            for (int i = 0; i < KeyCount; i++)
            {
                bool down = false, pressed = false;
                if (keyboard != null)
                    foreach (var physical in bindings[i])
                    {
                        var control = keyboard[physical];
                        down |= control.isPressed;
                        pressed |= control.wasPressedThisFrame;
                    }
                held[i] = down;
                // Fallback if an event was missed: keep the press, ordered after the recorded ones.
                if (pressed && !pressedThisFrame[i])
                {
                    pressedThisFrame[i] = true;
                    frame.Add(new InputPress((InputKey)i, InputState.currentTime));
                }
            }
        }
    }

    [DefaultExecutionOrder(-32000)]
    sealed class InputManagerUpdater : MonoBehaviour
    {
        void Update() => InputManager.OnPreUpdate();
    }
}
