using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NWOS.DesktopShell.Ui
{
    public static class IconHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000; // 32x32 Pixel
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource? GetIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Falls nur ein Programmname wie "notepad.exe" übergeben wurde, im Systempfad auflösen
            string resolvedPath = ResolveFullPath(path);

            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON;

            // Falls der Pfad nicht direkt existiert, trotzdem anhand der Dateiendung ein Icon anfordern
            if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            uint attributes = Directory.Exists(resolvedPath) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            IntPtr hImg = SHGetFileInfo(resolvedPath, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (hImg == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                ImageSource img = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                img.Freeze(); // Macht das Bild thread-sicher und performant für WPF
                return img;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon); // Wichtig: Native GDI-Ressource freigeben
            }
        }

        private static string ResolveFullPath(string fileName)
        {
            if (File.Exists(fileName) || Directory.Exists(fileName))
                return fileName;

            // Im Windows- und System32-Verzeichnis suchen (z. B. für "notepad.exe", "cmd.exe", "explorer.exe")
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string winCandidate = Path.Combine(winDir, fileName);
            if (File.Exists(winCandidate)) return winCandidate;

            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string sysCandidate = Path.Combine(sysDir, fileName);
            if (File.Exists(sysCandidate)) return sysCandidate;

            // In der PATH-Umgebungsvariable suchen
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), fileName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { /* Ungültige PATH-Einträge ignorieren */ }
                }
            }

            return fileName;
        }
    }
}