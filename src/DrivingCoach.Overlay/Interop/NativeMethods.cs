using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DrivingCoach.Overlay.Interop;

/// <summary>
/// Die Win32-Aufrufe, die WPF nicht anbietet: durchklickbare Fenster und
/// globale Tastenkürzel.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    internal const int GwlExStyle = -20;

    /// <summary>Mausklicks gehen durch das Fenster hindurch ans Spiel.</summary>
    internal const int WsExTransparent = 0x0000_0020;

    /// <summary>Voraussetzung für <see cref="WsExTransparent"/>.</summary>
    internal const int WsExLayered = 0x0008_0000;

    /// <summary>Hält das Fenster aus Alt-Tab heraus.</summary>
    internal const int WsExToolWindow = 0x0000_0080;

    /// <summary>Verhindert, dass ein Klick den Fokus vom Spiel wegnimmt.</summary>
    internal const int WsExNoActivate = 0x0800_0000;

    internal const int WmHotkey = 0x0312;

    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;

    /// <summary>Kein Dauerfeuer, solange die Taste gehalten wird.</summary>
    internal const uint ModNoRepeat = 0x4000;

    // GetWindowLongPtrW/SetWindowLongPtrW gibt es nur in 64-Bit-Prozessen.
    // Das Overlay läuft als 64-Bit-Anwendung; auf 32 Bit wären es die
    // Varianten ohne "Ptr".
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>Schaltet um, ob Mausklicks durch das Fenster fallen.</summary>
    internal static void SetClickThrough(nint handle, bool enabled)
    {
        nint style = GetWindowLongPtr(handle, GwlExStyle);

        nint updated = enabled
            ? style | WsExTransparent | WsExLayered
            : (style & ~WsExTransparent) | WsExLayered;

        SetWindowLongPtr(handle, GwlExStyle, updated);
    }

    /// <summary>Nimmt das Fenster aus Alt-Tab und aus der Fokusreihenfolge.</summary>
    internal static void MakePassiveOverlay(nint handle)
    {
        nint style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate | WsExLayered);
    }
}
