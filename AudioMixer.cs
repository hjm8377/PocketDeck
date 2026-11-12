using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal sealed class MixerChannel
{
    public string id { get; set; }
    public string name { get; set; }
    public int volume { get; set; }
}

internal static class AudioMixer
{
    public static List<MixerChannel> Read()
    {
        return Access(null, 0);
    }

    public static void SetVolume(string channel, int percent)
    {
        if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException("percent");
        Access(channel, percent);
    }

    private static List<MixerChannel> Access(string channel, int percent)
    {
        var channels = new List<MixerChannel>();
        object enumerator = new DeviceEnumeratorCom();
        IDevice device = null;
        try
        {
            ((IDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device);
            if (channel == null || channel == "master")
                channels.Add(Master(device, channel != null, percent));
            if (channel != "master")
                Sessions(device, channels, channel, percent);
            return channels;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    private static MixerChannel Master(IDevice device, bool set, int percent)
    {
        Guid iid = typeof(IEndpointVolume).GUID, context = Guid.Empty;
        object volume;
        device.Activate(ref iid, 23, IntPtr.Zero, out volume);
        try
        {
            var endpoint = (IEndpointVolume)volume;
            if (set) endpoint.SetMasterVolumeLevelScalar(percent / 100f, ref context);
            float scalar;
            endpoint.GetMasterVolumeLevelScalar(out scalar);
            return new MixerChannel { id = "master", name = "전체 볼륨", volume = Percent(scalar) };
        }
        finally { Release(volume); }
    }

    private static void Sessions(IDevice device, List<MixerChannel> channels, string selected, int percent)
    {
        Guid iid = typeof(ISessionManager).GUID, context = Guid.Empty;
        object manager;
        device.Activate(ref iid, 23, IntPtr.Zero, out manager);
        ISessionEnumerator sessions = null;
        bool found = false;
        try
        {
            ((ISessionManager)manager).GetSessionEnumerator(out sessions);
            int count;
            sessions.GetCount(out count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                object item = null;
                try
                {
                    sessions.GetSession(i, out item);
                    var control = (ISessionControl)item;
                    int state;
                    control.GetState(out state);
                    if (state == 2) continue;
                    string name, id;
                    if (control.IsSystemSoundsSession() == 0)
                    {
                        id = "system";
                        name = "시스템 소리";
                    }
                    else
                    {
                        uint pid;
                        control.GetProcessId(out pid);
                        name = ProcessName(pid);
                        if (name == null) continue;
                        id = "app:" + name.ToLowerInvariant();
                    }
                    var volume = (ISessionVolume)item;
                    if (selected != null)
                    {
                        if (id != selected) continue;
                        volume.SetMasterVolume(percent / 100f, ref context);
                        found = true;
                    }
                    else if (!seen.Contains(id))
                    {
                        float scalar;
                        volume.GetMasterVolume(out scalar);
                        seen.Add(id);
                        channels.Add(new MixerChannel { id = id, name = name, volume = Percent(scalar) });
                    }
                }
                catch (COMException) { /* A session may disappear while being enumerated. */ }
                catch (InvalidCastException) { /* Some sessions expose no per-session volume. */ }
                finally { Release(item); }
            }
        }
        finally
        {
            Release(sessions);
            Release(manager);
        }
        if (selected != null && !found) throw new InvalidOperationException("선택한 오디오 세션을 찾을 수 없어요.");
    }

    private static string ProcessName(uint pid)
    {
        try { using (var process = Process.GetProcessById((int)pid)) return process.ProcessName; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static int Percent(float scalar) { return (int)Math.Round(scalar * 100); }

    private static void Release(object com)
    {
        if (com != null) Marshal.ReleaseComObject(com);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint mask, out IntPtr collection);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr callback);
        void UnregisterControlChangeNotify(IntPtr callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float value, ref Guid context);
        void SetMasterVolumeLevelScalar(float value, ref Guid context);
        void GetMasterVolumeLevel(out float value);
        void GetMasterVolumeLevelScalar(out float value);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        void GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(ref Guid session, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out ISessionEnumerator enumerator);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl
    {
        void GetState(out int state);
        void GetDisplayName(out IntPtr name);
        void SetDisplayName(IntPtr name, ref Guid context);
        void GetIconPath(out IntPtr path);
        void SetIconPath(IntPtr path, ref Guid context);
        void GetGroupingParam(out Guid grouping);
        void SetGroupingParam(ref Guid grouping, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr events);
        void UnregisterAudioSessionNotification(IntPtr events);
        void GetSessionIdentifier(out IntPtr id);
        void GetSessionInstanceIdentifier(out IntPtr id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionVolume
    {
        void SetMasterVolume(float value, ref Guid context);
        void GetMasterVolume(out float value);
    }
}
