using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OurTaiko
{
    // A private session-bus connection owns the reservation, not a helper process or a saved
    // PipeWire profile. The bus drops the name even on SIGKILL. Keep serving the protocol while
    // holding it: WirePlumber must observe a real reservation owner before it can reclaim it.
    internal sealed class LinuxAudioReservation : IDisposable
    {
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
        const string Library = "libdbus-1.so.3";
        const string Interface = "org.freedesktop.ReserveDevice1";
        IntPtr connection;
        Thread worker;
        volatile bool stopping;
        readonly string name, path;

        public LinuxAudioReservation(int card)
        {
            name = Interface + ".Audio" + card;
            path = "/org/freedesktop/ReserveDevice1/Audio" + card;
            dbus_threads_init_default();
            try
            {
                connection = dbus_bus_get_private(0, IntPtr.Zero);
                if (connection == IntPtr.Zero) throw new InvalidOperationException("ALSA exclusive requires a session D-Bus connection");
                dbus_connection_set_exit_on_disconnect(connection, 0);
                if (dbus_bus_name_has_owner(connection, name, IntPtr.Zero) == 0)
                    throw new InvalidOperationException("No ReserveDevice1 manager for this card; shared output is still available");
                // Queue BEFORE RequestRelease: there must be no unowned gap when the manager yields.
                int result = dbus_bus_request_name(connection, name, 0, IntPtr.Zero);
                if (result == 2)
                {
                    var request = dbus_message_new_method_call(name, path, Interface, "RequestRelease");
                    if (request == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate audio reservation request");
                    try
                    {
                        int priority = int.MaxValue;
                        dbus_message_append_args(request, 'i', ref priority, 0);
                        var reply = dbus_connection_send_with_reply_and_block(connection, request, 3000, IntPtr.Zero);
                        if (reply == IntPtr.Zero) throw new InvalidOperationException("Audio reservation manager did not respond to RequestRelease");
                        try
                        {
                            if (GetInt(reply, IntPtr.Zero, 'b', out int accepted, 0) == 0 || accepted == 0)
                                throw new InvalidOperationException("Audio device reservation refused; the card may be in use");
                        }
                        finally { dbus_message_unref(reply); }
                    }
                    finally { dbus_message_unref(request); }
                    result = dbus_bus_request_name(connection, name, 0, IntPtr.Zero);
                }
                if (result != 1 && result != 4) throw new InvalidOperationException("Could not acquire the audio device reservation");
                worker = new Thread(Serve) { IsBackground = true, Name = "ALSA reservation" };
                worker.Start();
            }
            catch { Dispose(); throw; }
        }

        void Serve()
        {
            while (!stopping && dbus_connection_read_write(connection, 50) != 0)
            {
                IntPtr message;
                while ((message = dbus_connection_pop_message(connection)) != IntPtr.Zero)
                {
                    try { Respond(message); }
                    finally { dbus_message_unref(message); }
                }
            }
        }

        void Respond(IntPtr message)
        {
            if (Marshal.PtrToStringAnsi(dbus_message_get_path(message)) != path) return;
            IntPtr reply = IntPtr.Zero;
            if (dbus_message_is_method_call(message, Interface, "RequestRelease") != 0)
            {
                // Maximal protocol priority: no other client can preempt active game playback.
                reply = dbus_message_new_method_return(message);
                int refused = 0;
                dbus_message_append_args(reply, 'b', ref refused, 0);
            }
            else if (dbus_message_is_method_call(message, "org.freedesktop.DBus.Properties", "Get") != 0
                && GetStrings(message, IntPtr.Zero, 's', out var iface, 's', out var property, 0) != 0
                && Marshal.PtrToStringAnsi(iface) == Interface)
            {
                string key = Marshal.PtrToStringAnsi(property);
                if (key == "Priority" || key == "ApplicationName" || key == "ApplicationDeviceName")
                {
                    reply = dbus_message_new_method_return(message);
                    // DBusMessageIter is an opaque ABI struct; generously sized native storage avoids
                    // relying on its private pointer/int layout on different Linux architectures.
                    IntPtr iter = Marshal.AllocHGlobal(128), variant = Marshal.AllocHGlobal(128);
                    IntPtr text = Marshal.StringToHGlobalAnsi(key == "ApplicationDeviceName" ? name : "OurTaikoPlay");
                    try
                    {
                        dbus_message_iter_init_append(reply, iter);
                        dbus_message_iter_open_container(iter, 'v', key == "Priority" ? "i" : "s", variant);
                        if (key == "Priority")
                        {
                            int priority = int.MaxValue;
                            AppendInt(variant, 'i', ref priority);
                        }
                        else AppendString(variant, 's', ref text);
                        dbus_message_iter_close_container(iter, variant);
                    }
                    finally { Marshal.FreeHGlobal(text); Marshal.FreeHGlobal(variant); Marshal.FreeHGlobal(iter); }
                }
            }
            if (reply == IntPtr.Zero && dbus_message_get_type(message) == 1)
                reply = dbus_message_new_error(message, "org.freedesktop.DBus.Error.UnknownMethod", "Unsupported reservation method or property");
            if (reply == IntPtr.Zero) return;
            dbus_connection_send(connection, reply, IntPtr.Zero);
            dbus_message_unref(reply);
            dbus_connection_flush(connection);
        }

        public void Dispose()
        {
            stopping = true;
            worker?.Join();
            worker = null;
            if (connection == IntPtr.Zero) return;
            dbus_connection_close(connection);
            dbus_connection_unref(connection);
            connection = IntPtr.Zero;
        }

        [DllImport(Library)] static extern int dbus_threads_init_default();
        [DllImport(Library)] static extern IntPtr dbus_bus_get_private(int type, IntPtr error);
        [DllImport(Library)] static extern int dbus_bus_name_has_owner(IntPtr c, string name, IntPtr error);
        [DllImport(Library)] static extern int dbus_bus_request_name(IntPtr c, string name, uint flags, IntPtr error);
        [DllImport(Library)] static extern void dbus_connection_set_exit_on_disconnect(IntPtr c, int enabled);
        [DllImport(Library)] static extern IntPtr dbus_message_new_method_call(string destination, string path, string iface, string method);
        [DllImport(Library)] static extern IntPtr dbus_connection_send_with_reply_and_block(IntPtr c, IntPtr m, int timeout, IntPtr error);
        [DllImport(Library)] static extern int dbus_message_append_args(IntPtr m, int type, ref int value, int end);
        [DllImport(Library, EntryPoint = "dbus_message_get_args")] static extern int GetInt(IntPtr m, IntPtr error, int type, out int value, int end);
        [DllImport(Library, EntryPoint = "dbus_message_get_args")] static extern int GetStrings(IntPtr m, IntPtr error, int type, out IntPtr value, int type2, out IntPtr value2, int end);
        [DllImport(Library)] static extern int dbus_connection_read_write(IntPtr c, int timeout);
        [DllImport(Library)] static extern IntPtr dbus_connection_pop_message(IntPtr c);
        [DllImport(Library)] static extern int dbus_message_is_method_call(IntPtr m, string iface, string method);
        [DllImport(Library)] static extern IntPtr dbus_message_get_path(IntPtr m);
        [DllImport(Library)] static extern int dbus_message_get_type(IntPtr m);
        [DllImport(Library)] static extern IntPtr dbus_message_new_method_return(IntPtr m);
        [DllImport(Library)] static extern IntPtr dbus_message_new_error(IntPtr m, string name, string text);
        [DllImport(Library)] static extern void dbus_message_iter_init_append(IntPtr m, IntPtr iter);
        [DllImport(Library)] static extern int dbus_message_iter_open_container(IntPtr iter, int type, string signature, IntPtr child);
        [DllImport(Library, EntryPoint = "dbus_message_iter_append_basic")] static extern int AppendInt(IntPtr iter, int type, ref int value);
        [DllImport(Library, EntryPoint = "dbus_message_iter_append_basic")] static extern int AppendString(IntPtr iter, int type, ref IntPtr value);
        [DllImport(Library)] static extern int dbus_message_iter_close_container(IntPtr iter, IntPtr child);
        [DllImport(Library)] static extern int dbus_connection_send(IntPtr c, IntPtr m, IntPtr serial);
        [DllImport(Library)] static extern void dbus_connection_flush(IntPtr c);
        [DllImport(Library)] static extern void dbus_message_unref(IntPtr m);
        [DllImport(Library)] static extern void dbus_connection_close(IntPtr c);
        [DllImport(Library)] static extern void dbus_connection_unref(IntPtr c);
#else
        public LinuxAudioReservation(int card) => throw new PlatformNotSupportedException("ALSA exclusive requires Linux");
        public void Dispose() { }
#endif
    }
}
