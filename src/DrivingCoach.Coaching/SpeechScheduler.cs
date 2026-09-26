using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>
/// Entscheidet, ob und wann eine Meldung tatsächlich gesprochen wird.
/// </summary>
/// <remarks>
/// Ein Coach, der ununterbrochen redet, macht langsamer statt schneller.
/// Deshalb drei Regeln: Mindestabstand zwischen Ansagen, keine Wiederholung
/// desselben Satzes, und nichts in Momenten hoher Arbeitslast – also nicht
/// mitten im Anbremsen oder im Kurvenscheitel. Fahrfehler sind davon
/// ausgenommen, weil die Rückmeldung nur unmittelbar nach dem Fehler etwas nützt.
/// </remarks>
public sealed class SpeechScheduler(CoachOptions options)
{
    /// <summary>Ab diesem Bremsdruck ist der Fahrer beschäftigt.</summary>
    private const float BusyBrakeThreshold = 0.35f;

    /// <summary>Ab diesem Lenkeinschlag ist der Fahrer beschäftigt.</summary>
    private const float BusySteeringThreshold = 0.6f;

    private readonly Dictionary<string, double> _lastSpoken = [];
    private double _lastUtterance = double.NegativeInfinity;

    /// <summary>Prüft, ob die Meldung jetzt gesprochen werden darf.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="frame">Der aktuelle Fahrzustand.</param>
    public bool ShouldSpeak(CoachMessage message, in TelemetryFrame frame)
    {
        if (!options.SpeechEnabled || !message.HasSpeech)
        {
            return false;
        }

        double now = message.At;

        if (_lastSpoken.TryGetValue(message.SpeechText, out double last) &&
            now - last < options.SpeechRepeatBlockSeconds)
        {
            return false;
        }

        // Fahrfehler gehen vor: die Rückmeldung muss zum Ereignis gehören.
        bool urgent = message.Kind == CoachMessageKind.Fault;

        if (!urgent && now - _lastUtterance < options.MinimumSpeechGapSeconds)
        {
            return false;
        }

        if (!urgent && IsDriverBusy(in frame))
        {
            return false;
        }

        _lastUtterance = now;
        _lastSpoken[message.SpeechText] = now;
        return true;
    }

    /// <summary>Setzt die Sperren zurück, etwa bei Sitzungswechsel.</summary>
    public void Reset()
    {
        _lastSpoken.Clear();
        _lastUtterance = double.NegativeInfinity;
    }

    private static bool IsDriverBusy(in TelemetryFrame frame) =>
        frame.Brake > BusyBrakeThreshold || MathF.Abs(frame.Steering) > BusySteeringThreshold;
}
