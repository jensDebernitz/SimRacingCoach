using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>
/// Der Countdown auf den nächsten Bremspunkt.
/// </summary>
/// <param name="CornerName">Die Kurve, für die gebremst wird.</param>
/// <param name="MetresAway">Weg bis zum Bremspunkt, in Metern.</param>
/// <param name="WindowMetres">Weg, über den der Countdown läuft.</param>
/// <param name="IsNow">True, wenn jetzt getreten werden muss.</param>
/// <param name="Plan">
/// Wie fest zu treten ist, falls sich das aus der Referenzrunde ablesen lässt.
/// </param>
public readonly record struct BrakeCue(
    string CornerName,
    float MetresAway,
    float WindowMetres,
    bool IsNow,
    BrakePlan? Plan = null)
{
    /// <summary>0 am Anfang des Countdowns, 1 am Bremspunkt.</summary>
    public double Progress =>
        WindowMetres <= 1f ? 1.0 : Math.Clamp(1.0 - (MetresAway / WindowMetres), 0.0, 1.0);

    /// <summary>Die Zeile über dem Balken.</summary>
    public string Text
    {
        // Die Bremskraft steht schon im Countdown und nicht erst beim Tritt:
        // Wer weiß, dass gleich voll gebremst wird, stellt den Fuß anders auf,
        // als wenn es nur ein Anbremsen wird.
        get
        {
            string force = Plan is { } plan ? $"  ·  {plan.ForceText}" : string.Empty;

            return IsNow
                ? $"BREMSEN  ·  {CornerName}{force}"
                : $"Bremspunkt {CornerName}  ·  {MetresAway:0} m{force}";
        }
    }
}

/// <summary>
/// Findet, wo für eine Kurve gebremst werden muss, und baut daraus den
/// Countdown für die Anzeige.
/// </summary>
/// <remarks>
/// Bevorzugt wird der Bremspunkt der gerechneten Ideallinie: Sie weiß, mit
/// welchem Tempo sich der Scheitel überhaupt erreichen lässt, und wie lange die
/// Verzögerung dorthin dauert. Gibt es noch keine Linie, tritt der gemessene
/// Bremspunkt der Referenzrunde ein – der ist vorsichtiger, aber immer da,
/// sobald es überhaupt Kurven gibt.
/// </remarks>
public static class BrakeGuide
{
    /// <summary>So weit vor dem Scheitel wird nach dem Beginn der Bremsphase gesucht.</summary>
    private const float SearchMetres = 400f;

    /// <summary>Kürzere "Bremsphasen" sind Rauschen im Tempoprofil.</summary>
    private const float MinBrakingMetres = 15f;

    /// <summary>Tempounterschied, ab dem eine Verzögerung als solche zählt, in m/s.</summary>
    private const float SpeedNoise = 0.05f;

    /// <summary>Der Countdown läuft über so viele Sekunden Fahrweg.</summary>
    private const float WindowSeconds = 3.5f;

    /// <summary>Kürzer wird das Fenster nicht – sonst blitzt es in langsamen Kurven nur auf.</summary>
    private const float MinWindowMetres = 70f;

    /// <summary>So lange vor dem Bremspunkt gilt er als erreicht.</summary>
    private const float NowSeconds = 0.35f;

    /// <summary>
    /// Die Bremspunkte aller Kurven, in Metern ab Start/Ziel.
    /// <see cref="float.NaN"/> steht für eine Kurve, die ohne Bremsen geht.
    /// </summary>
    public static float[] BrakePoints(
        IReadOnlyList<Corner> corners,
        RecordedLap? reference,
        IdealLine? line)
    {
        var points = new float[corners.Count];

        for (int i = 0; i < corners.Count; i++)
        {
            points[i] = BrakePointOf(corners[i], reference, line) ?? float.NaN;
        }

        return points;
    }

    /// <summary>
    /// Die Bremskraft-Vorgabe je Kurve, in derselben Reihenfolge wie die
    /// übergebenen Bremspunkte. <c>null</c> steht für eine Kurve, über deren
    /// Bremsdruck sich nichts sagen lässt.
    /// </summary>
    /// <remarks>
    /// Nimmt die fertigen Bremspunkte entgegen, statt sie selbst zu suchen:
    /// Vorgabe und Punkt müssen zwingend zur selben Stelle gehören, sonst
    /// beschriebe die Kraft eine andere Bremsphase als der Countdown.
    /// </remarks>
    public static BrakePlan?[] Plans(IReadOnlyList<float> brakePoints, RecordedLap? reference)
    {
        var plans = new BrakePlan?[brakePoints.Count];

        for (int i = 0; i < brakePoints.Count; i++)
        {
            plans[i] = float.IsNaN(brakePoints[i]) ? null : BrakePlan.For(reference, brakePoints[i]);
        }

        return plans;
    }

    /// <summary>
    /// Wo für diese Kurve gebremst werden muss, in Metern ab Start/Ziel.
    /// <c>null</c>, wenn die Kurve ohne Bremsen geht.
    /// </summary>
    public static float? BrakePointOf(Corner corner, RecordedLap? reference, IdealLine? line)
    {
        if (reference is null || reference.BinSize <= 0f)
        {
            return null;
        }

        if (line is not null && FromIdealLine(corner, reference, line) is { } fromLine)
        {
            return fromLine;
        }

        return corner.HasBrakingZone ? corner.BrakingStartBin * reference.BinSize : null;
    }

    /// <summary>
    /// Sucht vom Scheitel aus rückwärts, bis das Solltempo aufhört zu fallen.
    /// </summary>
    /// <remarks>
    /// Gesucht wird über die Distanz, nicht über Abschnitts-Indizes: Der
    /// Scheitel steht in Abschnitten der Referenzrunde, die Linie rechnet in
    /// ihren eigenen. Beide haben zwar dieselbe Abschnittslänge, aber darauf
    /// muss sich diese Suche nicht verlassen.
    /// </remarks>
    private static float? FromIdealLine(Corner corner, RecordedLap reference, IdealLine line)
    {
        float apex = corner.ApexBin * reference.BinSize;
        float step = line.BinSize;

        if (step <= 0f)
        {
            return null;
        }

        float point = apex;
        float travelled = 0f;

        while (travelled < SearchMetres)
        {
            // Rückwärts betrachtet steigt das Tempo, solange die Bremsphase
            // reicht: Vor einer Verzögerung war das Auto schneller.
            if (line.SpeedAt(point - step) <= line.SpeedAt(point) + SpeedNoise)
            {
                break;
            }

            point -= step;
            travelled += step;
        }

        return travelled < MinBrakingMetres ? null : Wrap(point, reference.TrackLength);
    }

    /// <summary>
    /// Der Countdown für die Anzeige, oder <c>null</c>, wenn gerade keiner
    /// ansteht.
    /// </summary>
    public static BrakeCue? CueFor(CoachState state, in TelemetryFrame frame)
    {
        if (!frame.IsDriving
            || state.UpcomingCorner is not { } corner
            || state.UpcomingBrakePoint is not { } point)
        {
            return null;
        }

        float length = TrackLengthOf(state);
        if (length < 1f)
        {
            return null;
        }

        float away = MetresTo(point, frame.LapDistance, length);
        float window = MathF.Max(frame.Speed * WindowSeconds, MinWindowMetres);

        if (away > window)
        {
            return null;
        }

        return new BrakeCue(
            corner.Name,
            away,
            window,
            away <= MathF.Max(frame.Speed * NowSeconds, 8f),
            state.UpcomingBrakePlan);
    }

    /// <summary>
    /// Sucht die Bremsphase, in der das Auto gerade steckt. <c>null</c>, wenn
    /// zwischen zwei Bremspunkten gefahren wird.
    /// </summary>
    /// <remarks>
    /// Getrennt von <see cref="CueFor"/>, weil das zwei verschiedene Zeitpunkte
    /// sind: Der Countdown endet am Bremspunkt, die Bremsphase fängt dort erst
    /// an. Die nächste Kurve ist dann auch schon die übernächste – wer beides
    /// aus derselben Angabe zöge, bekäme im Anbremsen die Zahlen der folgenden
    /// Kurve zu sehen.
    /// </remarks>
    public static ActiveBraking? ActiveZone(
        IReadOnlyList<Corner> corners,
        IReadOnlyList<float> brakePoints,
        IReadOnlyList<BrakePlan?> plans,
        float lapDistance,
        float trackLength)
    {
        if (trackLength < 1f)
        {
            return null;
        }

        int count = Math.Min(corners.Count, Math.Min(brakePoints.Count, plans.Count));

        for (int i = 0; i < count; i++)
        {
            if (plans[i] is not { } plan || float.IsNaN(brakePoints[i]))
            {
                continue;
            }

            // Über Start/Ziel hinweg gerechnet. Ein Bremspunkt, der noch vor uns
            // liegt, kommt hier als fast ganze Runde heraus und fällt damit von
            // selbst aus dem Fenster.
            float travelled = lapDistance - brakePoints[i];
            if (travelled < 0f)
            {
                travelled += trackLength;
            }

            if (travelled <= plan.EndMetres)
            {
                return new ActiveBraking(corners[i], plan, plan.EndMetres - travelled);
            }
        }

        return null;
    }

    /// <summary>
    /// Soll und Ist am Bremspedal für die Anzeige, oder <c>null</c>, wenn
    /// gerade keine Bremsphase läuft.
    /// </summary>
    public static BrakeForceCue? ForceCueFor(CoachState state, in TelemetryFrame frame)
    {
        if (!frame.IsDriving || state.Braking is not { } braking)
        {
            return null;
        }

        // Der Sollwert kommt laufend aus der Referenzrunde, nicht aus den zwei
        // Stufen der Vorgabe: Die Stufen sind zum Sagen da, die Anzeige kann der
        // gemessenen Kurve folgen. Fehlt die Referenz, bleibt der Spitzendruck.
        PedalTarget? target = PedalGuide.TargetAt(state.Reference, frame.LapDistance);

        BrakePlan plan = braking.Plan;
        bool releaseAhead = plan.HasRelease && braking.MetresLeft > plan.ReleaseAtMetres;

        return new BrakeForceCue(
            braking.Corner.Name,
            target?.Brake ?? plan.Peak,
            frame.Brake,
            releaseAhead ? braking.MetresLeft - plan.ReleaseAtMetres : float.NaN,
            plan.ReleaseTo);
    }

    /// <summary>Weg bis zu einer Distanz, über Start/Ziel hinweg gerechnet.</summary>
    public static float MetresTo(float target, float lapDistance, float trackLength)
    {
        float ahead = target - lapDistance;
        return ahead < 0f ? ahead + trackLength : ahead;
    }

    /// <summary>Die Streckenlänge, die zu den Bremspunkten passt.</summary>
    public static float TrackLengthOf(CoachState state) =>
        state.Reference?.TrackLength ?? state.Session.TrackLength;

    private static float Wrap(float distance, float length)
    {
        if (length < 1f)
        {
            return distance;
        }

        float wrapped = distance % length;
        return wrapped < 0f ? wrapped + length : wrapped;
    }
}
