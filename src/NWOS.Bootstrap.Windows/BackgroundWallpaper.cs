using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NWOS.Bootstrap.Windows;

internal static class BackgroundWallpaper
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;

    private static readonly string ImagePath = Path.Combine(
        AppContext.BaseDirectory,
        "Bilder-NWOS",
        "Hintergrund-Wasserzeichen",
        "NWOS-Logo-Standart Hintergund.png");

    internal static void Apply()
    {
        try
        {
            if (!File.Exists(ImagePath))
            {
                Console.WriteLine($"[NWOS Core] Hintergrundbild fehlt: {ImagePath}");
                return;
            }

            if (!SystemParametersInfo(
                    SpiSetDesktopWallpaper,
                    0,
                    ImagePath,
                    SpifUpdateIniFile | SpifSendWinIniChange))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Das NWOS-Hintergrundbild konnte nicht angewendet werden.");
            }

            Console.WriteLine("[NWOS Core] NWOS-Hintergrundbild wurde angewendet.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NWOS Core] Hintergrundbild konnte nicht angewendet werden: {ex.Message}");
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        string pvParam,
        uint fWinIni);
}
