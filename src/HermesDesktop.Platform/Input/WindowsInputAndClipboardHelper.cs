using System.Runtime.InteropServices;
using HermesDesktop.Core.Rpc;

namespace HermesDesktop.Platform.Input;

/// <summary>
/// Windows native fallbacks for Win32 screenshot clipboard formats (PNG / CF_DIBV5 / CF_DIB)
/// and invoking the OS/IME voice-to-text typing overlay (Win+H on Windows).
/// </summary>
public static class WindowsInputAndClipboardHelper
{
    private const uint CfDib = 8;
    private const uint CfDibV5 = 17;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkLWin = 0x5B;
    private const ushort VkH = 0x48;

    public static bool TryTriggerSystemVoiceDictation()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var inputs = new[]
            {
                CreateKeyInput(VkLWin, keyUp: false),
                CreateKeyInput(VkH, keyUp: false),
                CreateKeyInput(VkH, keyUp: true),
                CreateKeyInput(VkLWin, keyUp: true)
            };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            return sent == inputs.Length;
        }
        catch
        {
            return false;
        }
    }

    public static (byte[] Bytes, string MimeType)? TryReadClipboardImageBytes()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var opened = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    opened = true;
                    break;
                }
                Thread.Sleep(15);
            }
            if (!opened) return null;

            try
            {
                var pngFormat = RegisterClipboardFormatW("PNG");
                if (pngFormat != 0 && IsClipboardFormatAvailable(pngFormat))
                {
                    var pngBytes = ReadClipboardFormatBytes(pngFormat);
                    if (pngBytes is { Length: > 0 })
                    {
                        return (pngBytes, "image/png");
                    }
                }

                foreach (var fmt in new[] { CfDibV5, CfDib })
                {
                    if (!IsClipboardFormatAvailable(fmt)) continue;
                    var raw = ReadClipboardFormatBytes(fmt);
                    if (raw is { Length: > 0 })
                    {
                        var bmpBytes = ClipboardImageHelper.WrapRawDibAsBmpIfNeeded(raw);
                        return (bmpBytes, "image/bmp");
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            // Ignore transient Win32 clipboard lock errors
        }

        return null;
    }

    private static byte[]? ReadClipboardFormatBytes(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;

        var size = (int)Math.Min(GlobalSize(handle), (nuint)(50 * 1024 * 1024));
        if (size <= 0) return null;

        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var buffer = new byte[size];
            Marshal.Copy(ptr, buffer, 0, size);
            return buffer;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KeyEventFKeyUp : 0,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
