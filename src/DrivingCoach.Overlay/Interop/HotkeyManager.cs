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
/// <para>
/// Die Kombinationen liegen bewusst auf Strg+Alt+Buchstabe: die F-Tasten sind
/// in AMS2 mit Kameras, Boxenfunk und Ansichten belegt.
/// </para>
/// <para>
/// Jede Aktion hat mehrere Kandidaten, weil <c>RegisterHotKey</c> systemweit
/// arbeitet: Wer zuerst kommt, mahlt zuerst. Strg+Alt+M und Strg+Alt+R greifen
/// auf vielen Rechnern schon anderswo hin – Lenkrad-Software, Aufnahme-Programme
/// und Herstellerwerkzeuge bedienen sich gern in derselben Ecke. Deshalb wird
/// nicht eine Taste versucht und aufgegeben, sondern die Reihe durchprobiert
/// und hinterher angezeigt, welche es geworden ist.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HotkeyManager : IDisposable
{
    private const uint VkA = 0x41;
    private const uint VkB = 0x42;
    private const uint VkC = 0x43;
    private const uint VkD = 0x44;
    private const uint VkE = 0x45;
    private const uint VkF = 0x46;
    private const uint VkG = 0x47;
    private const uint VkH = 0x48;
    private const uint VkI = 0x49;
    private const uint VkJ = 0x4A;
    private const uint VkK = 0x4B;
    private const uint VkL = 0x4C;
    private const uint VkM = 0x4D;
    private const uint VkN = 0x4E;
    private const uint VkO = 0x4F;
    private const uint VkP = 0x50;
    private const uint VkQ = 0x51;
    private const uint VkR = 0x52;
    private const uint VkS = 0x53;
    private const uint VkT = 0x54;
    private const uint VkU = 0x55;
    private const uint VkV = 0x56;
    private const uint VkW = 0x57;
    private const uint VkX = 0x58;
    private const uint VkY = 0x59;
    private const uint VkZ = 0x5A;

    private const uint VkEnd = 0x23;
    private const uint VkDelete = 0x2E;

    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;

    private const uint VkNumpad4 = 0x64;
    private const uint VkNumpad6 = 0x66;
    private const uint VkNumpad8 = 0x68;
    private const uint VkNumpad2 = 0x62;

    /// <param name="Keys">
    /// Die Wunschtaste zuerst, danach Ausweichtasten in absteigender
    /// Eignung. Die erste, die sich anmelden lässt, gewinnt.
    /// </param>
    /// <param name="InHelp">
    /// Ob das Kürzel in der Hilfe unten im Overlay steht. Die Pfeiltasten
    /// wirken nur beim Einmessen und werden dort erklärt; in der Dauerhilfe
    /// wären sie vier Zeilen, die die meiste Zeit niemanden angehen.
    /// </param>
    private static readonly (HotkeyAction Action, uint[] Keys, string Description, bool InHelp)[] Bindings =
    [
        (HotkeyAction.ToggleVisibility, [VkO, VkH, VkY], "Overlay ein/aus", true),
        (HotkeyAction.ToggleMoveMode, [VkM, VkV, VkW, VkJ], "Overlay verschieben", true),
        (HotkeyAction.ToggleSpeech, [VkS, VkP, VkU], "Sprachausgabe ein/aus", true),
        (HotkeyAction.ToggleLine, [VkL, VkI, VkG], "Ideallinie ein/aus", true),
        (HotkeyAction.ToggleCalibration, [VkE, VkN, VkB], "Linie einmessen", true),
        (HotkeyAction.ResetReference, [VkR, VkZ, VkX], "Referenzrunde verwerfen", true),
        (HotkeyAction.AskQuestion, [VkK, VkA, VkD], "Coach fragen", true),
        (HotkeyAction.SessionSummary, [VkF, VkC, VkT], "Fazit der Session", true),

        // Ohne dieses Kürzel gäbe es keinen Weg zurück: das Fenster steht
        // weder in der Taskleiste noch in Alt-Tab und hat keinen Schließen-Knopf.
        // Entsprechend viele Kandidaten – eines davon muss sitzen.
        (HotkeyAction.Quit, [VkQ, VkEnd, VkDelete], "Coach beenden", true),

        // Auch mit Strg+Alt, obwohl sie nur beim Einmessen etwas tun: Blanke
        // Pfeiltasten systemweit zu belegen, würde jede andere Anwendung
        // lahmlegen, solange der Coach läuft. Der Ziffernblock als Ausweichweg,
        // falls die Pfeiltasten schon vergeben sind.
        (HotkeyAction.CalibrationNext, [VkRight, VkNumpad6], "nächster Wert", false),
        (HotkeyAction.CalibrationPrevious, [VkLeft, VkNumpad4], "vorheriger Wert", false),
        (HotkeyAction.CalibrationIncrease, [VkUp, VkNumpad8], "Wert erhöhen", false),
        (HotkeyAction.CalibrationDecrease, [VkDown, VkNumpad2], "Wert verringern", false),
    ];

    private readonly List<int> _registered = [];
    private HwndSource? _source;

    /// <summary>Wird im UI-Thread ausgelöst, wenn ein Kürzel gedrückt wurde.</summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>
    /// Die Belegung, wie sie tatsächlich zustande kam – für die Hilfe im
    /// Overlay. Vor <see cref="Attach"/> leer.
    /// </summary>
    public IReadOnlyList<string> Descriptions { get; private set; } = [];

    /// <summary>
    /// Aktionen, für die keine einzige Taste frei war. Bleibt im Normalfall
    /// leer; steht hier etwas, fehlt die Funktion wirklich.
    /// </summary>
    public IReadOnlyList<string> Failed { get; private set; } = [];

    /// <summary>
    /// Kürzel, die auf einer Ausweichtaste gelandet sind. Der Fahrer soll
    /// erfahren, dass sich etwas verschoben hat, und nicht auf der Wunschtaste
    /// herumdrücken.
    /// </summary>
    public IReadOnlyList<string> Moved { get; private set; } = [];

    public void Attach(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        var descriptions = new List<string>();
        var failed = new List<string>();
        var moved = new List<string>();

        foreach ((HotkeyAction action, uint[] keys, string description, bool inHelp) in Bindings)
        {
            uint? key = Register(handle, action, keys);

            if (key is null)
            {
                failed.Add(description);
                continue;
            }

            string label = $"Strg+Alt+{NameOf(key.Value)} – {description}";

            if (inHelp)
            {
                descriptions.Add(label);
            }

            // Nicht die Wunschtaste: Das gehört gesagt, egal ob das Kürzel
            // sonst in der Dauerhilfe stünde.
            if (key.Value != keys[0])
            {
                moved.Add($"{description}: Strg+Alt+{NameOf(keys[0])} war belegt, jetzt Strg+Alt+{NameOf(key.Value)}");
            }
        }

        Descriptions = descriptions;
        Failed = failed;
        Moved = moved;
    }

    /// <summary>
    /// Probiert die Tasten der Reihe nach durch und gibt die erste zurück, die
    /// sich anmelden ließ.
    /// </summary>
    private uint? Register(nint handle, HotkeyAction action, uint[] keys)
    {
        int id = (int)action;

        foreach (uint key in keys)
        {
            if (!NativeMethods.RegisterHotKey(
                    handle, id, NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, key))
            {
                continue;
            }

            _registered.Add(id);
            return key;
        }

        return null;
    }

    /// <summary>Der Name einer Taste, wie er in der Hilfe stehen soll.</summary>
    private static string NameOf(uint key) => key switch
    {
        VkLeft => "←",
        VkRight => "→",
        VkUp => "↑",
        VkDown => "↓",
        VkNumpad4 => "Ziffernblock 4",
        VkNumpad6 => "Ziffernblock 6",
        VkNumpad8 => "Ziffernblock 8",
        VkNumpad2 => "Ziffernblock 2",
        VkEnd => "Ende",
        VkDelete => "Entf",

        // Die Buchstabentasten tragen ihren ASCII-Code als Virtual-Key-Code.
        >= 0x41 and <= 0x5A => ((char)key).ToString(),

        _ => $"0x{key:X2}",
    };

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
