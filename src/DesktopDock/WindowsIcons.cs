using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Media.Imaging;

namespace DesktopDock;

/// <summary>
/// Asks the Windows shell for the icon it shows for a file, folder or program,
/// so pinned apps look exactly like they do in the Start menu.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsIcons
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr HIcon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    // Classic DllImport: the source-generated form cannot marshal SHFILEINFO's
    // fixed-length string fields.
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path, uint fileAttributes, ref ShFileInfo info, uint sizeOfInfo, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Returns the shell icon for a target, or null when there is not one.</summary>
    public static Bitmap? Extract(string target, int size)
    {
        string path = Environment.ExpandEnvironmentVariables(target ?? string.Empty);
        if (path.Length == 0)
        {
            return null;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            bool isDirectory = Directory.Exists(path);
            var info = default(ShFileInfo);
            uint flags = ShgfiIcon | ShgfiLargeIcon;
            uint attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;

            if (!isDirectory && !File.Exists(path))
            {
                // Unknown or missing target: fall back to the icon for its file type.
                flags |= ShgfiUseFileAttributes;
            }

            SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            handle = info.HIcon;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            using var icon = System.Drawing.Icon.FromHandle(handle);
            using var drawing = icon.ToBitmap();
            using var stream = new MemoryStream();
            drawing.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                DestroyIcon(handle);
            }
        }
    }
}
