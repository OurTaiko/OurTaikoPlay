using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ManagedBass;

namespace OurTaiko
{
    public sealed class LinuxAudioDevice
    {
        public string Id { get; }
        public string Name { get; }
        public string Driver { get; }
        public int BassIndex { get; }
        public int Card { get; }
        public int Pcm { get; }

        public LinuxAudioDevice(string id, string name, string driver, int bassIndex, int card, int pcm)
        {
            Id = id; Name = name; Driver = driver; BassIndex = bassIndex; Card = card; Pcm = pcm;
        }

        public string HardwareParameters()
        {
            try
            {
                foreach (string substream in Directory.EnumerateDirectories($"/proc/asound/card{Card}/pcm{Pcm}p", "sub*"))
                {
                    var owner = Regex.Match(File.ReadAllText(Path.Combine(substream, "status")), @"owner_pid\s*:\s*(\d+)");
                    if (!owner.Success || !Directory.Exists("/proc/self/task/" + owner.Groups[1].Value)) continue;
                    string text = File.ReadAllText(Path.Combine(substream, "hw_params")).Trim();
                    if (text != "closed") return "ALSA actual: " + text.Replace("\n", "; ") + "; period_size / buffer_size in frames";
                }
                return "ALSA negotiated parameters unavailable";
            }
            catch (IOException) { return "ALSA negotiated parameters unavailable"; }
            catch (UnauthorizedAccessException) { return "ALSA negotiated parameters unavailable"; }
        }

        public void CheckUnmanagedUsers()
        {
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
            // Do not tear down the manager's card while an uncooperative raw client holds a
            // PCM. Recreating it while still occupied can leave the shared server on a dummy
            // sink. Session audio servers can yield through ReserveDevice1; other owners cannot.
            foreach (string pcm in Directory.EnumerateDirectories($"/proc/asound/card{Card}", "pcm*"))
                foreach (string sub in Directory.EnumerateDirectories(pcm, "sub*"))
                {
                    var owner = Regex.Match(File.ReadAllText(Path.Combine(sub, "status")), @"owner_pid\s*:\s*(\d+)");
                    if (!owner.Success) continue;
                    string pid = owner.Groups[1].Value;
                    try
                    {
                        var group = Regex.Match(File.ReadAllText("/proc/" + pid + "/status"), @"Tgid:\s*(\d+)");
                        string process = File.ReadAllText("/proc/" + (group.Success ? group.Groups[1].Value : pid) + "/comm").Trim();
                        if (LinuxAudioDevices.IsSessionAudioServer(process)) continue;
                    }
                    catch (IOException) { if (!Directory.Exists("/proc/" + pid)) continue; }
                    catch (UnauthorizedAccessException) { }
                    throw new InvalidOperationException(Name + ": ALSA card is busy with a native client outside the session audio server; reservation left unchanged");
                }
#endif
        }
    }

    public static class LinuxAudioDevices
    {
        public static bool IsSessionAudioServer(string processName)
            => processName == "pipewire" || processName == "pulseaudio" || processName == "jackd" || processName == "jackdbus";

        // The physical sysfs device path survives ALSA card-number reordering. USB identity is
        // port-specific: moving an interface to a different port deliberately requires reselection.
        // Never persist BASS's enumeration index, ALSA's card number, or a display name.
        public static string StableId(string physicalPath, int pcm, string hardwareIdentity = "")
            => "sysfs:" + physicalPath + "|hardware:" + Uri.EscapeDataString(hardwareIdentity) + "|pcm:" + pcm;

        public static LinuxAudioDevice Resolve(IReadOnlyList<LinuxAudioDevice> devices, string id)
        {
            LinuxAudioDevice match = null;
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var device in devices)
            {
                if (!string.Equals(device.Id, id, StringComparison.Ordinal)) continue;
                if (match != null) return null; // Ambiguity must never select a different output.
                match = device;
            }
            return match;
        }

        public static IReadOnlyList<LinuxAudioDevice> Enumerate(out string failure)
        {
            failure = null;
            var devices = new List<LinuxAudioDevice>();
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
            try
            {
                for (int index = 1; Bass.GetDeviceInfo(index, out var info); index++)
                {
                    var match = Regex.Match(info.Driver ?? "", @"^hw:(\d+),(\d+)$");
                    if (!info.IsEnabled || !match.Success) continue;
                    int card = int.Parse(match.Groups[1].Value), pcm = int.Parse(match.Groups[2].Value);
                    IntPtr resolved = realpath($"/sys/class/sound/card{card}/device", IntPtr.Zero);
                    if (resolved == IntPtr.Zero) continue;
                    string physical;
                    try { physical = Marshal.PtrToStringAnsi(resolved); }
                    finally { free(resolved); }
                    devices.Add(new LinuxAudioDevice(StableId(physical, pcm, HardwareIdentity(physical)), info.Name, info.Driver, index, card, pcm));
                }
                if (devices.Count == 0) failure = "No ALSA hardware playback devices found";
            }
            catch (Exception error) { failure = "ALSA device enumeration failed: " + error.Message; }
#else
            failure = "ALSA exclusive requires Linux";
#endif
            return devices;
        }

#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
        static string HardwareIdentity(string physical)
        {
            var values = new List<string>();
            // Include hardware attributes as well as the port, so replacing a USB interface at
            // the same port cannot silently redirect the saved selection to unrelated hardware.
            var directory = new DirectoryInfo(physical);
            for (int level = 0; level < 3 && directory != null; level++, directory = directory.Parent)
            {
                foreach (string attribute in new[] { "serial", "idVendor", "idProduct", "vendor", "device", "subsystem_vendor", "subsystem_device" })
                {
                    string file = Path.Combine(directory.FullName, attribute);
                    if (File.Exists(file)) values.Add(level + ":" + attribute + "=" + File.ReadAllText(file).Trim());
                }
            }
            return string.Join(";", values);
        }

        [DllImport("libc.so.6")] static extern IntPtr realpath(string path, IntPtr resolved);
        [DllImport("libc.so.6")] static extern void free(IntPtr pointer);
#endif
    }
}
