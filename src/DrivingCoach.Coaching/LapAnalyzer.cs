using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Vergleicht eine gefahrene Runde Kurve für Kurve mit der Referenzrunde und
/// formuliert daraus konkrete Hinweise.
/// </summary>
/// <remarks>
/// Die Kurvengrenzen stammen immer aus der Referenzrunde. Würde jede Runde
/// eigene Grenzen bekommen, verschöben sich die Vergleichsfenster und der
/// Zeitverlust ließe sich nicht mehr sauber einer Kurve zuordnen.
/// </remarks>
public sealed class LapAnalyzer
{
    /// <summary>Unterhalb dieses Zeitverlusts lohnt kein Hinweis.</summary>
    private const float MinRelevantTimeLoss = 0.05f;

    /// <summary>Ab hier gilt das Bremspedal als betätigt (wie im <see cref="CornerDetector"/>).</summary>
    private const float BrakeThreshold = 0.08f;

    /// <summary>Ab hier gilt "wieder voll am Gas".</summary>
    private const float ThrottleThreshold = 0.85f;

    /// <summary>Suchfenster vor dem Referenz-Bremspunkt.</summary>
    private const float BrakeSearchBeforeMetres = 150f;

    /// <summary>Suchfenster hinter dem Referenz-Bremspunkt.</summary>
    private const float BrakeSearchAfterMetres = 40f;

    /// <summary>Puffer vor der Bremszone, ab dem der Zeitverlust gemessen wird.</summary>
    private const float MeasureLeadInMetres = 20f;

    /// <summary>Puffer hinter der Kurve – der Ausgang wirkt sich erst auf der Geraden aus.</summary>
    private const float MeasureRunOutMetres = 80f;

    /// <summary>Ein Bremspunkt gilt erst ab dieser Abweichung als zu früh/spät.</summary>
    private const float BrakePointToleranceMetres = 6f;

    /// <summary>Scheiteltempo gilt erst ab dieser Abweichung als zu niedrig (m/s ≈ 3,6 km/h).</summary>
    private const float ApexSpeedToleranceMps = 1.0f;

    /// <summary>Gaspunkt gilt erst ab dieser Abweichung als zu spät.</summary>
    private const float ThrottleToleranceMetres = 8f;

    /// <summary>Wertet eine Runde gegen die Referenz aus.</summary>
    /// <param name="lap">Die gefahrene Runde.</param>
    /// <param name="reference">Die Referenzrunde.</param>
    /// <param name="corners">Die auf der Referenz erkannten Kurven.</param>
    public LapAnalysis Analyze(RecordedLap lap, RecordedLap reference, IReadOnlyList<Corner> corners)
    {
        var results = new List<CornerAdvice>(corners.Count);

        foreach (Corner corner in corners)
        {
            results.Add(AnalyzeCorner(lap, reference, corner));
        }

        IReadOnlyList<CornerSequence> sequences =
            SequenceAnalyzer.Detect(corners, reference.BinSize, reference.TrackLength);

        return new LapAnalysis(lap.LapTime, reference.LapTime, results)
        {
            Sequences = SequenceAnalyzer.Analyze(sequences, results),
        };
    }

    private static CornerAdvice AnalyzeCorner(RecordedLap lap, RecordedLap reference, Corner corner)
    {
        float binSize = reference.BinSize;

        float brakingStartDistance = corner.BrakingStartBin * binSize;
        float entryDistance = MathF.Max(0f, brakingStartDistance - MeasureLeadInMetres);
        float exitDistance = MathF.Min(
            reference.TrackLength,
            (corner.EndBin * binSize) + MeasureRunOutMetres);

        float timeLost =
            DeltaEngine.DeltaAt(lap, reference, exitDistance) -
            DeltaEngine.DeltaAt(lap, reference, entryDistance);

        float brakePointDelta = MeasureBrakePointDelta(lap, reference, corner);
        float apexSpeedDelta = MeasureApexSpeed(lap, reference, corner) - corner.ApexSpeed;
        float throttleDelta = MeasureThrottlePointDelta(lap, reference, corner);

        (AdviceKind kind, string text, string speech) = Describe(
            corner, timeLost, brakePointDelta, apexSpeedDelta, throttleDelta);

        // Die Fahrweise wird immer gemessen, auch wenn die Kurve zeitlich
        // passt: Ein Fehler, der auf dieser Strecke nichts kostet, kostet auf
        // der nächsten trotzdem.
        TechniqueReport technique = TechniqueAnalyzer.Compare(
            TechniqueAnalyzer.Measure(lap, corner, reference.BinSize),
            TechniqueAnalyzer.Measure(reference, corner, reference.BinSize),
            corner);

        return new CornerAdvice(
            corner, kind, timeLost, brakePointDelta, apexSpeedDelta, throttleDelta, text, speech)
        {
            Technique = technique,
        };
    }

    /// <summary>
    /// Wo hat der Fahrer gebremst, verglichen mit der Referenz?
    /// Positiv bedeutet später, negativ früher.
    /// </summary>
    private static float MeasureBrakePointDelta(RecordedLap lap, RecordedLap reference, Corner corner)
    {
        float binSize = reference.BinSize;
        float referenceBrake = corner.BrakingStartBin * binSize;

        if (!corner.HasBrakingZone)
        {
            return 0f;
        }

        float from = MathF.Max(0f, referenceBrake - BrakeSearchBeforeMetres);
        float to = referenceBrake + BrakeSearchAfterMetres;

        for (float distance = from; distance <= to; distance += binSize)
        {
            float brake = RecordedLap.Sample(lap.Channels.Brake, lap.BinSize, distance);
            if (brake > BrakeThreshold)
            {
                return distance - referenceBrake;
            }
        }

        // Gar nicht gebremst, wo die Referenz bremst: als "deutlich später" werten.
        return BrakeSearchAfterMetres;
    }

    /// <summary>Niedrigste Geschwindigkeit der Runde im Kurvenbereich der Referenz.</summary>
    private static float MeasureApexSpeed(RecordedLap lap, RecordedLap reference, Corner corner)
    {
        float binSize = reference.BinSize;
        float minSpeed = float.MaxValue;

        for (int bin = corner.StartBin; bin <= corner.EndBin; bin++)
        {
            float speed = RecordedLap.Sample(lap.Channels.Speed, lap.BinSize, bin * binSize);
            minSpeed = MathF.Min(minSpeed, speed);
        }

        return minSpeed == float.MaxValue ? 0f : minSpeed;
    }

    /// <summary>
    /// Wo ging der Fahrer wieder voll aufs Gas, verglichen mit der Referenz?
    /// Positiv bedeutet später.
    /// </summary>
    private static float MeasureThrottlePointDelta(RecordedLap lap, RecordedLap reference, Corner corner)
    {
        float binSize = reference.BinSize;
        float referenceThrottle = corner.ThrottleBin * binSize;

        float from = corner.ApexBin * binSize;
        float to = MathF.Min(reference.TrackLength, (corner.EndBin * binSize) + MeasureRunOutMetres);

        for (float distance = from; distance <= to; distance += binSize)
        {
            float throttle = RecordedLap.Sample(lap.Channels.Throttle, lap.BinSize, distance);
            if (throttle >= ThrottleThreshold)
            {
                return distance - referenceThrottle;
            }
        }

        return to - referenceThrottle;
    }

    /// <summary>
    /// Wählt den wahrscheinlichsten Hauptgrund für den Zeitverlust.
    /// </summary>
    /// <remarks>
    /// Die Gewichtung ist eine Heuristik: Abweichungen werden auf eine
    /// gemeinsame Skala gebracht (1 m/s Scheiteltempo entspricht etwa 10 m
    /// Brems- oder Gaspunkt) und der größte Posten gewinnt. Das trifft nicht
    /// jeden Einzelfall, liefert aber genau einen umsetzbaren Hinweis statt
    /// einer Zahlenkolonne.
    /// </remarks>
    private static (AdviceKind Kind, string Text, string Speech) Describe(
        Corner corner,
        float timeLost,
        float brakePointDelta,
        float apexSpeedDelta,
        float throttleDelta)
    {
        if (timeLost < -MinRelevantTimeLoss)
        {
            return (AdviceKind.Faster,
                $"{corner.Name}: gut – {-timeLost:0.00} s schneller als die Referenz",
                string.Empty);
        }

        if (timeLost <= MinRelevantTimeLoss)
        {
            return (AdviceKind.Neutral, $"{corner.Name}: passt", string.Empty);
        }

        float apexKmh = apexSpeedDelta * 3.6f;

        // Klassischer Anfängerfehler: spät anbremsen, dabei den Scheitel verpassen.
        if (brakePointDelta > BrakePointToleranceMetres * 1.5f && apexSpeedDelta < -ApexSpeedToleranceMps)
        {
            return (AdviceKind.Overdriving,
                $"{corner.Name}: zu spät gebremst, dadurch {-apexKmh:0} km/h zu langsam am Scheitel · {timeLost:0.00} s",
                $"{corner.Name}: früher anbremsen, dann trägt es dich nicht über den Scheitelpunkt.");
        }

        float brakeScore = brakePointDelta < -BrakePointToleranceMetres ? -brakePointDelta / 10f : 0f;
        float apexScore = apexSpeedDelta < -ApexSpeedToleranceMps ? -apexSpeedDelta : 0f;
        float throttleScore = throttleDelta > ThrottleToleranceMetres ? throttleDelta / 10f : 0f;

        if (brakeScore >= apexScore && brakeScore >= throttleScore && brakeScore > 0f)
        {
            return (AdviceKind.BrakeLater,
                $"{corner.Name}: {-brakePointDelta:0} m später bremsen · {timeLost:0.00} s",
                $"{corner.Name}: du bremst zu früh.");
        }

        if (apexScore >= throttleScore && apexScore > 0f)
        {
            return (AdviceKind.MoreApexSpeed,
                $"{corner.Name}: {-apexKmh:0} km/h mehr am Scheitel · {timeLost:0.00} s",
                $"{corner.Name}: mehr Tempo am Scheitelpunkt.");
        }

        if (throttleScore > 0f)
        {
            return (AdviceKind.EarlierThrottle,
                $"{corner.Name}: {throttleDelta:0} m früher aufs Gas · {timeLost:0.00} s",
                $"{corner.Name}: früher aufs Gas.");
        }

        // Zeit verloren, aber keine der Einzelgrößen sticht heraus – meist eine
        // unsaubere Linie oder Korrekturen mitten in der Kurve.
        return (AdviceKind.MoreApexSpeed,
            $"{corner.Name}: unsauber durchgefahren · {timeLost:0.00} s",
            $"{corner.Name}: ruhiger und runder fahren.");
    }
}
