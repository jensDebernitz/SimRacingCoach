using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Threading;

namespace DrivingCoach.Overlay.Speech;

/// <summary>
/// Spricht die Hinweise des Coaches über die Windows-Sprachausgabe (SAPI).
/// </summary>
/// <remarks>
/// <para>
/// Angebunden über späte COM-Bindung an <c>SAPI.SpVoice</c> statt über das
/// NuGet-Paket <c>System.Speech</c>. Das ist Absicht: SAPI gehört zu Windows,
/// damit hat die gesamte Projektmappe keine einzige externe Abhängigkeit und
/// baut auch ohne Zugriff auf einen Paket-Server.
/// </para>
/// <para>
/// Kein Cloud-Dienst, weil die Ansagen unmittelbar kommen müssen – eine halbe
/// Sekunde Netzverzögerung macht einen Kurventipp wertlos.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CoachVoice : IDisposable
{
    /// <summary>Ansage läuft im Hintergrund weiter, der Aufruf kehrt sofort zurück.</summary>
    private const int SpeakAsync = 1;

    /// <summary>Verwirft, was noch aussteht. Beim Fahren zählt nur die neueste Information.</summary>
    private const int SpeakPurgeBefore = 2;

    /// <summary>Deutsch (Deutschland, Schweiz, Österreich) als SAPI-Sprachkennung.</summary>
    private const string GermanLanguages = "Language=407;807;c07";

    /// <summary>
    /// Der Dispatcher des Threads, auf dem das COM-Objekt erzeugt wurde.
    /// SAPI-Aufrufe laufen ausschließlich hierüber – ein COM-Objekt quer über
    /// Apartment-Grenzen anzusprechen ist genau die Art Fehler, die sich erst
    /// mitten im Rennen zeigt.
    /// </summary>
    private readonly Dispatcher _dispatcher;

    private object? _voice;
    private bool _disposed;

    public CoachVoice(int rate = 2, int volume = 90)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        try
        {
            Type? type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                Problem = "SAPI.SpVoice ist auf diesem System nicht registriert.";
                return;
            }

            _voice = Activator.CreateInstance(type);
            if (_voice is null)
            {
                Problem = "SAPI.SpVoice ließ sich nicht erzeugen.";
                return;
            }

            // Etwas schneller als normal: die Ansage soll in die Lücke zwischen
            // zwei Kurven passen.
            SetProperty(_voice, "Rate", Math.Clamp(rate, -10, 10));
            SetProperty(_voice, "Volume", Math.Clamp(volume, 0, 100));

            VoiceName = SelectGermanVoice(_voice);
            IsGerman = VoiceName is not null;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException
                                      or MissingMethodException or InvalidCastException
                                      or NotSupportedException)
        {
            // Ohne Audiogerät oder ohne Stimme läuft das Overlay stumm weiter –
            // ein fehlender Lautsprecher darf den Coach nicht lahmlegen.
            Release();
            Problem = ex.Message;
        }
    }

    /// <summary>True, wenn überhaupt gesprochen werden kann.</summary>
    public bool IsAvailable => _voice is not null;

    /// <summary>True, wenn eine deutsche Stimme gefunden wurde.</summary>
    public bool IsGerman { get; }

    /// <summary>Name der verwendeten Stimme, sonst <c>null</c>.</summary>
    public string? VoiceName { get; }

    /// <summary>Grund, falls die Sprachausgabe nicht startbereit ist.</summary>
    public string? Problem { get; }

    /// <summary>Sprachausgabe vorübergehend abschalten.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Sagt einen Satz an. Wird aus dem Telemetrie-Thread aufgerufen und auf
    /// den Erzeuger-Thread umgeleitet. Eine noch laufende Ansage wird verworfen.
    /// </summary>
    public void Say(string text)
    {
        if (_disposed || !Enabled || _voice is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Post(() => Speak(text, SpeakAsync | SpeakPurgeBefore));
    }

    /// <summary>Bricht die laufende Ansage ab.</summary>
    public void Stop()
    {
        if (_disposed || _voice is null)
        {
            return;
        }

        // Ein leerer Text mit "Purge" ist der von SAPI vorgesehene Weg,
        // die Warteschlange zu leeren.
        Post(() => Speak(string.Empty, SpeakAsync | SpeakPurgeBefore));
    }

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Send, action);
    }

    private void Speak(string text, int flags)
    {
        object? voice = _voice;
        if (voice is null)
        {
            return;
        }

        try
        {
            Call(voice, "Speak", text, flags);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidComObjectException)
        {
            // Stummer Ausfall ist besser als ein Absturz mitten in der Kurve.
        }
    }

    /// <summary>
    /// Sucht eine deutsche Stimme und stellt sie ein. Ohne passende Stimme
    /// bleibt die Systemstimme aktiv – dann werden die Ansagen englisch
    /// ausgesprochen, sind aber immer noch verständlich.
    /// </summary>
    private static string? SelectGermanVoice(object voice)
    {
        object? tokens = Call(voice, "GetVoices", GermanLanguages, string.Empty);
        if (tokens is null)
        {
            return null;
        }

        if (GetProperty(tokens, "Count") is not int count || count == 0)
        {
            return null;
        }

        object? token = Call(tokens, "Item", 0);
        if (token is null)
        {
            return null;
        }

        // "Voice" ist eine Objekt-Eigenschaft und braucht PutRef, nicht Put.
        voice.GetType().InvokeMember(
            "Voice",
            BindingFlags.PutRefDispProperty,
            binder: null,
            target: voice,
            args: [token],
            culture: CultureInfo.InvariantCulture);

        return Call(token, "GetDescription", 0) as string;
    }

    /// <summary>Kurzer Satz für die Statuszeile des Overlays.</summary>
    public string StatusLine => !IsAvailable
        ? $"Sprachausgabe nicht verfügbar ({Problem})"
        : IsGerman
            ? $"Stimme: {VoiceName}"
            : "Keine deutsche Stimme installiert – Windows-Einstellungen → Zeit und Sprache → Sprache → Sprachpaket hinzufügen";

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target: target,
            args: args,
            culture: CultureInfo.InvariantCulture);

    private static object? GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target: target,
            args: null,
            culture: CultureInfo.InvariantCulture);

    private static void SetProperty(object target, string name, object value) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            binder: null,
            target: target,
            args: [value],
            culture: CultureInfo.InvariantCulture);

    private void Release()
    {
        object? voice = Interlocked.Exchange(ref _voice, null);
        if (voice is null || !Marshal.IsComObject(voice))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(voice);
        }
        catch (ArgumentException)
        {
            // Bereits freigegeben.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_voice is null)
        {
            return;
        }

        Speak(string.Empty, SpeakAsync | SpeakPurgeBefore);
        Release();
    }
}
