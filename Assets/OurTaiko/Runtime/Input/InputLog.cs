using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace OurTaiko
{
    // Diagnostics for lost hits, tag "Input", at Info level. Devices are always logged as they come
    // and go. With Settings → General → Input Log on (Enabled), every layer a press passes is logged
    // too, so a session's log shows where a hit went missing:
    //   event     a device's state event: the buttons it changed and those it left held (every device
    //             but mice, touchscreens and pens; a keyboard event that changed no key is logged too)
    //   text      a character the keyboard typed, what a text box would get
    //   poll      a press the event hook missed and the frame's poll found
    //   pad       a touch or click on an on-screen drum zone
    //   publish   a press handed to this frame's scripts (after the input delay)
    //   judge     a lane judging a press, with the judgment
    //   drop      a lane dropping a press, with the reason
    public static class InputLog
    {
        public const string Tag = "Input";
        public static bool Enabled { get; set; }

        static readonly StringBuilder changes = new StringBuilder(), held = new StringBuilder();
        static readonly HashSet<Keyboard> typing = new HashSet<Keyboard>();
        static bool installed;

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            foreach (var device in InputSystem.devices) { Log.Info(Tag, "Device present: " + Describe(device)); WatchText(device); }
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed && device is Keyboard keyboard) typing.Remove(keyboard);
            else WatchText(device);
            Log.Info(Tag, "Device " + change + ": " + Describe(device));
        }

        static void WatchText(InputDevice device)
        {
            if (!(device is Keyboard keyboard) || !typing.Add(keyboard)) return;
            keyboard.onTextInput += c =>
            {
                if (Enabled) Log.Info(Tag, $"text {Printable(c)} from {Short(keyboard)} t={InputState.currentTime:F4} frame {Time.frameCount}");
            };
        }

        public static string Describe(InputDevice device)
        {
            var d = device.description;
            var text = new StringBuilder(Short(device)).Append(" layout ").Append(device.layout)
                .Append(" \"").Append(device.displayName).Append('"');
            if (!string.IsNullOrEmpty(d.interfaceName)) text.Append(" interface ").Append(d.interfaceName);
            if (!string.IsNullOrEmpty(d.manufacturer)) text.Append(" maker \"").Append(d.manufacturer).Append('"');
            if (!string.IsNullOrEmpty(d.product)) text.Append(" product \"").Append(d.product).Append('"');
            if (!device.native) text.Append(" virtual");
            if (!device.enabled) text.Append(" disabled");
            if (device is Keyboard && device == Keyboard.current) text.Append(" current");
            return text.ToString();
        }

        static string Short(InputDevice device) => device.name + "#" + device.deviceId;
        static string Printable(char c) => char.IsControl(c) ? $"U+{(int)c:X4}" : "'" + c + "'";

        // Called by InputManager for each event before its own filters, while the device still holds
        // the state before the event.
        internal static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!Enabled || device is Pointer) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            changes.Clear(); held.Clear();
            foreach (var control in device.allControls)
            {
                if (!(control is ButtonControl button) || button.synthetic || button.noisy) continue;
                if (!button.ReadValueFromEvent(eventPtr, out float value)) continue;
                bool pressed = button.IsValueConsideredPressed(value);
                if (pressed != button.isPressed)
                {
                    changes.Append(' ').Append(button.name).Append(pressed ? " down" : " up");
                    if (button is KeyControl key && InputManager.TryGetAction(key.keyCode, out var action))
                        changes.Append(" (").Append(action).Append(')');
                }
                else if (pressed) held.Append(' ').Append(button.name);
            }
            // Other devices may stream unchanged reports; a keyboard event with no change is a clue.
            if (changes.Length == 0 && !(device is Keyboard)) return;
            Log.Info(Tag, $"event {Short(device)} t={eventPtr.time:F4} now={InputState.currentTime:F4} frame {Time.frameCount} " +
                $"{InputState.currentUpdateType}:{(changes.Length == 0 ? " no button changed" : changes.ToString())}" +
                (held.Length > 0 ? " | held" + held : ""));
        }

        internal static void Poll(InputKey key) => Write($"poll {key} found by the frame's poll, not the event hook");

        internal static void Pad(InputKey key, InputDevice device, double time)
            => Write($"pad {key} from {Short(device)} t={time:F4}");

        internal static void Publish(InputPress press, double now)
            => Write($"publish {press.Key} t={press.Time:F4} waited {(now - press.Time) * 1000:F1} ms");

        public static void Judge(int seat, bool ka, bool right, double chartTime, string result)
            => Write($"judge {Seat(seat)} {(right ? "right" : "left")} {(ka ? "ka" : "don")} at chart {chartTime:F4}: {result}");

        public static void Drop(int seat, InputPress press, string reason)
            => Write($"drop {Seat(seat)} {press.Key} t={press.Time:F4}: {reason}");

        static string Seat(int seat) => seat == 0 ? "1P" : "2P";

        static void Write(string message)
        {
            if (Enabled) Log.Info(Tag, message + $" frame {Time.frameCount}");
        }
    }
}
