using System;
using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // Android can expose a Unity Keyboard even on a touch-only phone. Ask Android about the
    // real devices instead; this only controls settings visibility, never delivery of key events.
    public static class PhysicalInputDevices
    {
        const int KeyboardSource = 0x00000101, GamepadSource = 0x00000401, JoystickSource = 0x01000010;
        const int AlphabeticKeyboard = 2;
        // Android KEYCODE_A..Z, 0..9 and NUMPAD_0..9. Sparse keyboards/drums may not be marked
        // alphabetic; accept their typing keys without mistaking power/volume buttons for a keyboard.
        static readonly int[] TypingKeys = Enumerable.Range(29, 26).Concat(Enumerable.Range(7, 10))
            .Concat(Enumerable.Range(144, 10)).ToArray();
        static bool reportedFailure;

        public static bool IsPhysicalControlDevice(bool isVirtual, int sources, int keyboardType, bool hasTypingKeys)
        {
            if (isVirtual) return false;
            return (sources & GamepadSource) == GamepadSource || (sources & JoystickSource) == JoystickSource
                || (sources & KeyboardSource) == KeyboardSource && (keyboardType == AlphabeticKeyboard || hasTypingKeys);
        }

        public static bool Available()
        {
            if (Application.platform != RuntimePlatform.Android) return true;
            try
            {
                using var input = new AndroidJavaClass("android.view.InputDevice");
                foreach (int id in input.CallStatic<int[]>("getDeviceIds"))
                {
                    using var device = input.CallStatic<AndroidJavaObject>("getDevice", id);
                    // A device can disappear between listing its id and reading it.
                    if (device == null || device.Call<bool>("isVirtual")) continue;
                    int sources = device.Call<int>("getSources");
                    int keyboardType = device.Call<int>("getKeyboardType");
                    if (IsPhysicalControlDevice(false, sources, keyboardType, false)) return true;
                    if ((sources & KeyboardSource) != KeyboardSource) continue;
                    var keys = device.Call<bool[]>("hasKeys", (object)TypingKeys);
                    if (Array.Exists(keys, present => present)) return true;
                }
                return false;
            }
            catch (Exception e)
            {
                // A vendor/API failure must not lock a player out of their saved bindings.
                if (!reportedFailure) Log.Warn("Input", $"Physical device detection failed: {e.Message}");
                reportedFailure = true;
                return true;
            }
        }
    }
}
