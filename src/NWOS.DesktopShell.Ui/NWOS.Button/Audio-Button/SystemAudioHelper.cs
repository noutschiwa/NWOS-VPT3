using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NWOS.DesktopShell.Ui
{
    public sealed class AppAudioSessionInfo
    {
        public uint ProcessId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public float VolumePercent { get; init; }
        public bool IsMuted { get; init; }
    }

    public static class SystemAudioHelper
    {
        public static float GetMasterVolume()
        {
            IAudioEndpointVolume? volumeControl = null;
            try
            {
                volumeControl = GetAudioEndpointVolume();
                if (volumeControl == null) return 50f;

                volumeControl.GetMasterVolumeLevelScalar(out float level);
                return level * 100f;
            }
            catch
            {
                return 50f;
            }
            finally
            {
                if (volumeControl != null)
                    Marshal.ReleaseComObject(volumeControl);
            }
        }

        public static void SetMasterVolume(float volumePercent)
        {
            IAudioEndpointVolume? volumeControl = null;
            try
            {
                volumeControl = GetAudioEndpointVolume();
                if (volumeControl == null) return;

                float clamped = Math.Clamp(volumePercent / 100f, 0f, 1f);
                Guid eventContext = Guid.Empty;
                volumeControl.SetMasterVolumeLevelScalar(clamped, ref eventContext);
            }
            catch { }
            finally
            {
                if (volumeControl != null)
                    Marshal.ReleaseComObject(volumeControl);
            }
        }

        public static bool GetMute()
        {
            IAudioEndpointVolume? volumeControl = null;
            try
            {
                volumeControl = GetAudioEndpointVolume();
                if (volumeControl == null) return false;

                volumeControl.GetMute(out bool isMuted);
                return isMuted;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (volumeControl != null)
                    Marshal.ReleaseComObject(volumeControl);
            }
        }

        public static void SetMute(bool mute)
        {
            IAudioEndpointVolume? volumeControl = null;
            try
            {
                volumeControl = GetAudioEndpointVolume();
                if (volumeControl == null) return;

                Guid eventContext = Guid.Empty;
                volumeControl.SetMute(mute, ref eventContext);
            }
            catch { }
            finally
            {
                if (volumeControl != null)
                    Marshal.ReleaseComObject(volumeControl);
            }
        }

        public static List<AppAudioSessionInfo> GetActiveAppSessions()
        {
            var result = new List<AppAudioSessionInfo>();
            var seenProcessIds = new HashSet<uint>();

            IMMDeviceEnumerator? deviceEnumerator = null;
            IMMDevice? speakers = null;
            IAudioSessionManager2? sessionManager = null;
            IAudioSessionEnumerator? sessionEnumerator = null;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out speakers);
                if (speakers == null) return result;

                Guid iidSessionManager2 = typeof(IAudioSessionManager2).GUID;
                speakers.Activate(ref iidSessionManager2, 23, IntPtr.Zero, out object mgrObj);
                sessionManager = mgrObj as IAudioSessionManager2;
                if (sessionManager == null) return result;

                sessionManager.GetSessionEnumerator(out sessionEnumerator);
                if (sessionEnumerator == null) return result;

                sessionEnumerator.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl? control = null;
                    try
                    {
                        sessionEnumerator.GetSession(i, out control);
                        if (control == null) continue;

                        if (control is IAudioSessionControl2 control2 && control is ISimpleAudioVolume simpleVolume)
                        {
                            control2.GetProcessId(out uint pid);

                            // System-Sounds (PID 0) und doppelte Sessions desselben Prozesses überspringen
                            if (pid == 0 || !seenProcessIds.Add(pid))
                                continue;

                            simpleVolume.GetMasterVolume(out float level);
                            simpleVolume.GetMute(out bool isMuted);

                            string appName = ResolveSessionDisplayName(control2, pid);

                            result.Add(new AppAudioSessionInfo
                            {
                                ProcessId = pid,
                                DisplayName = appName,
                                VolumePercent = level * 100f,
                                IsMuted = isMuted
                            });
                        }
                    }
                    finally
                    {
                        if (control != null) Marshal.ReleaseComObject(control);
                    }
                }
            }
            catch { }
            finally
            {
                if (sessionEnumerator != null) Marshal.ReleaseComObject(sessionEnumerator);
                if (sessionManager != null) Marshal.ReleaseComObject(sessionManager);
                if (speakers != null) Marshal.ReleaseComObject(speakers);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }

            return result;
        }

        public static void SetSessionVolume(uint targetProcessId, float volumePercent)
        {
            IMMDeviceEnumerator? deviceEnumerator = null;
            IMMDevice? speakers = null;
            IAudioSessionManager2? sessionManager = null;
            IAudioSessionEnumerator? sessionEnumerator = null;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out speakers);
                if (speakers == null) return;

                Guid iidSessionManager2 = typeof(IAudioSessionManager2).GUID;
                speakers.Activate(ref iidSessionManager2, 23, IntPtr.Zero, out object mgrObj);
                sessionManager = mgrObj as IAudioSessionManager2;
                if (sessionManager == null) return;

                sessionManager.GetSessionEnumerator(out sessionEnumerator);
                if (sessionEnumerator == null) return;

                sessionEnumerator.GetCount(out int count);
                float clamped = Math.Clamp(volumePercent / 100f, 0f, 1f);
                Guid eventContext = Guid.Empty;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl? control = null;
                    try
                    {
                        sessionEnumerator.GetSession(i, out control);
                        if (control is IAudioSessionControl2 control2 && control is ISimpleAudioVolume simpleVolume)
                        {
                            control2.GetProcessId(out uint pid);
                            if (pid == targetProcessId)
                            {
                                simpleVolume.SetMasterVolume(clamped, ref eventContext);
                            }
                        }
                    }
                    finally
                    {
                        if (control != null) Marshal.ReleaseComObject(control);
                    }
                }
            }
            catch { }
            finally
            {
                if (sessionEnumerator != null) Marshal.ReleaseComObject(sessionEnumerator);
                if (sessionManager != null) Marshal.ReleaseComObject(sessionManager);
                if (speakers != null) Marshal.ReleaseComObject(speakers);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }
        }

        private static IAudioEndpointVolume? GetAudioEndpointVolume()
        {
            IMMDeviceEnumerator? deviceEnumerator = null;
            IMMDevice? speakers = null;
            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                // eRender = 0 (Wiedergabegerät), eMultimedia = 1
                deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out speakers);
                if (speakers == null) return null;

                Guid iidIAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
                speakers.Activate(ref iidIAudioEndpointVolume, 23, IntPtr.Zero, out object epvObj);
                return epvObj as IAudioEndpointVolume;
            }
            finally
            {
                if (speakers != null) Marshal.ReleaseComObject(speakers);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }
        }

        private static string ResolveSessionDisplayName(IAudioSessionControl2 control2, uint pid)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    return proc.MainWindowTitle;

                return proc.ProcessName;
            }
            catch
            {
                control2.GetDisplayName(out string displayName);
                return string.IsNullOrWhiteSpace(displayName) ? $"App (PID {pid})" : displayName;
            }
        }

        #region Core Audio COM Interfaces

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IAudioSessionControl SessionControl);
            int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
            int RegisterSessionNotification(IntPtr SessionNotification);
            int UnregisterSessionNotification(IntPtr SessionNotification);
            int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
            int UnregisterDuckNotification(IntPtr duckNotification);
        }

        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int SessionCount);
            int GetSession(int SessionCount, out IAudioSessionControl Session);
        }

        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int pRetVal);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        }

        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            int GetState(out int pRetVal);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int GetProcessId(out uint pRetVal);
            int IsSystemSoundsSession();
            int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float fLevel, ref Guid EventContext);
            int GetMasterVolume(out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        #endregion
    }
}