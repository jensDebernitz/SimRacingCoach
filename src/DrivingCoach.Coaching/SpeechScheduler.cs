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
    private double _lastBrakeCall = double.NegativeInfinity;
    private double _lastReleaseCall = double.NegativeInfinity;

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

        // Der Bremsruf lebt von der Wiederholung: "Bremsen" heißt an jeder
        // Kurve dasselbe und muss trotzdem jedes Mal kommen. Er bekommt deshalb
        // weder Wiederholsperre noch Arbeitslast-Prüfung – wer gleich bremsen
        // soll, ist per Definition gerade beschäftigt.
        //
        // Er läuft außerdem auf einer eigenen Uhr, getrennt von den übrigen
        // Ansagen. Sonst verschluckt ihn ausgerechnet der Kurventipp, der
        // dieselbe Kurve meint: Der kommt 2,5 s vorher, der Bremsruf 1,2 s
        // vorher – auf einer gemeinsamen Uhr wäre der Abstand zu kurz, und die
        // Kurve, über die der Coach gerade geredet hat, bekäme als einzige
        // keinen Bremspunkt. Dass "Bremsen" dem Tipp ins Wort fällt, ist
        // gewollt: SAPI verwirft mit "Purge" das Laufende, und eine Sekunde vor
        // dem Bremspunkt ist der Rest des Satzes ohnehin verloren.
        if (message.Kind == CoachMessageKind.BrakePoint)
        {
            if (now - _lastBrakeCall < options.BrakeCallMinGapSeconds)
            {
                return false;
            }

            _lastBrakeCall = now;
            return true;
        }

        // Der Löseruf gehört zum Bremsruf von eben und bekommt aus denselben
        // Gründen dieselben Ausnahmen. Seine Uhr ist aber noch einmal eine
        // eigene: In einer kurzen Bremszone liegen "Bremsen" und "Lösen auf 70"
        // keine zwei Sekunden auseinander, und ausgerechnet vom zugehörigen
        // Bremsruf darf ihn nichts verschlucken. Zwei Löserufe kurz
        // hintereinander – Schikane – werden dagegen weiter zu einem.
        if (message.Kind == CoachMessageKind.BrakeRelease)
        {
            if (now - _lastReleaseCall < options.BrakeCallMinGapSeconds)
            {
                return false;
            }

            _lastReleaseCall = now;
            return true;
        }

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
        _lastBrakeCall = double.NegativeInfinity;
        _lastReleaseCall = double.NegativeInfinity;
    }

    private static bool IsDriverBusy(in TelemetryFrame frame) =>
        frame.Brake > BusyBrakeThreshold || MathF.Abs(frame.Steering) > BusySteeringThreshold;
}
