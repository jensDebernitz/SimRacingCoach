namespace DrivingCoach.Coaching;

/// <summary>
/// Liefert vorformulierte Fassungen für Ansagen, die während der Fahrt
/// ausgelöst werden.
/// </summary>
/// <remarks>
/// <para>
/// Der Zugriff passiert im Telemetrie-Thread, unmittelbar bevor gesprochen
/// wird. Deshalb die harte Vorgabe: <see cref="TryGet"/> darf niemals
/// blockieren, kein Netz anfassen und nichts sperren, was länger als ein paar
/// Nanosekunden gehalten wird. Wer hier eine Anfrage stellt, hält die
/// Telemetrieschleife an.
/// </para>
/// <para>
/// Ersetzt wird ausschließlich der <em>gesprochene</em> Text. Die Zahlen im
/// Overlay bleiben die gerechneten – eine umformulierte Zahl wäre eine Zahl,
/// für die niemand mehr geradesteht.
/// </para>
/// </remarks>
public interface IPhraseSource
{
    /// <summary>Sucht eine Fassung zum Schlüssel. False heißt: den Standardsatz nehmen.</summary>
    bool TryGet(string key, out string speech);
}

/// <summary>Die Schlüssel, unter denen Ansagen abgelegt werden.</summary>
public static class PhraseKeys
{
    /// <summary>Kurventipp für die genannte Kurvennummer.</summary>
    public static string Corner(int number) => $"corner:{number}";

    /// <summary>Warnung zu einem Fahrfehler.</summary>
    public static string Fault(FaultKind kind) => $"fault:{kind}";
}
