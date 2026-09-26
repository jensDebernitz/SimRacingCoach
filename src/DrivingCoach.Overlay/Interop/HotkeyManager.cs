using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;

namespace DrivingCoach.Overlay.Interop;

/// <summary>Welche Aktion ein Tastenkürzel auslöst.</summary>
internal enum HotkeyAction
{
    ToggleVisibility,
    ToggleMoveMode,
    ToggleSpeech,
    ToggleLine,
    ResetReference,
    AskQuestion,
    SessionSummary,
    Quit,

    /// <summary>Einmessen der perspektivischen Linie ein- und ausschalten.</summary>
    ToggleCalibration,

    /// <summary>Nächster beziehungsweise vorheriger Wert beim Einmessen.</summary>
    CalibrationNext,
    CalibrationPrevious,

    /// <summary>Den ausgewählten Wert vergrößern oder verkleinern.</summary>
    CalibrationIncrease,
    CalibrationDecrease,
}

/// <summary>
/// Meldet systemweite Tastenkürzel an, damit sie auch greifen, während AMS2
/// den Fokus hat.
/// </summary>
/// <remarks>
/// Die Kombinationen liegen bewusst auf Strg+Alt+Buchstabe: die F-Tasten sind
/// in AMS2 mit Kameras, Boxenfunk und Ansichten belegt.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HotkeyManager : IDisposable
{
    private const uint VkO = 0x4F;
    private const uint VkM = 0x4D;
    private const uint VkS = 0x53;
    private const uint VkL = 0x4C;
    private const uint VkR = 0x52;
    private const uint VkK = 0x4B;
    private const uint VkF = 0x46;
    private const uint VkQ = 0x51;
    private const uint VkE = 0x45;

    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;

    /// <param name="InHelp">
    /// Ob das Kürzel in der Hilfe unten im Overlay steht. Die Pfeiltasten
    /// wirken nur beim Einmessen und werden dort erklärt; in der Dauerhilfe
    /// wären sie vier Zeilen, die die meiste Zeit niemanden angehen.
    /// </param>
    private static readonly (HotkeyAction Action, uint Key, string Label, bool InHelp)[] Bindings =
    [
        (HotkeyAction.ToggleVisibility, VkO, "Strg+Alt+O – Overlay ein/aus", true),
        (HotkeyAction.ToggleMoveMode, VkM, "Strg+Alt+M – Overlay verschieben", true),
        (HotkeyAction.ToggleSpeech, VkS, "Strg+Alt+S – Sprachausgabe ein/aus", true),
        (HotkeyAction.ToggleLine, VkL, "Strg+Alt+L – Ideallinie ein/aus", true),
        (HotkeyAction.ToggleCalibration, VkE, "Strg+Alt+E – Linie einmessen", true),
        (HotkeyAction.ResetReference, VkR, "Strg+Alt+R – Referenzrunde verwerfen", true),
        (HotkeyAction.AskQuestion, VkK, "Strg+Alt+K – Coach fragen", true),
        (HotkeyAction.SessionSummary, VkF, "Strg+Alt+F – Fazit der Session", true),

        // Ohne dieses Kürzel gäbe es keinen Weg zurück: das Fenster steht
        // weder in der Taskleiste noch in Alt-Tab und hat keinen Schließen-Knopf.
        (HotkeyAction.Quit, VkQ, "Strg+Alt+Q – Coach beenden", true),

        // Auch mit Strg+Alt, obwohl sie nur beim Einmessen etwas tun: Blanke
        // Pfeiltasten systemweit zu belegen, würde jede andere Anwendung
        // lahmlegen, solange der Coach läuft.
        (HotkeyAction.CalibrationNext, VkRight, "Strg+Alt+→ – nächster Wert", false),
        (HotkeyAction.CalibrationPrevious, VkLeft, "Strg+Alt+← – vorheriger Wert", false),
        (HotkeyAction.CalibrationIncrease, VkUp, "Strg+Alt+↑ – Wert erhöhen", false),
        (HotkeyAction.CalibrationDecrease, VkDown, "Strg+Alt+↓ – Wert verringern", false),
    ];

    private readonly List<int> _registered = [];
    private HwndSource? _source;

    /// <summary>Wird im UI-Thread ausgelöst, wenn ein Kürzel gedrückt wurde.</summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>Kürzel, die nicht belegt werden konnten – etwa weil ein anderes Programm sie hält.</summary>
    public IReadOnlyList<string> Failed { get; private set; } = [];

    /// <summary>Die Belegung als Text für die Hilfe im Overlay.</summary>
    public static IEnumerable<string> Descriptions =>
        Bindings.Where(b => b.InHelp).Select(b => b.Label);

    public void Attach(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        var failed = new List<string>();

        foreach ((HotkeyAction action, uint key, string label, _) in Bindings)
        {
            int id = (int)action;
            if (NativeMethods.RegisterHotKey(
                    handle, id, NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, key))
            {
                _registered.Add(id);
            }
            else
            {
                failed.Add(label);
            }
        }

        Failed = failed;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey)
        {
            return nint.Zero;
        }

        var action = (HotkeyAction)(int)wParam;
        if (Enum.IsDefined(action))
        {
            Pressed?.Invoke(action);
            handled = true;
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        foreach (int id in _registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source = null;
    }
}
