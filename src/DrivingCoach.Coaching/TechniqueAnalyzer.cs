using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Misst, <em>wie</em> eine Kurve gefahren wurde – Einlenkpunkt, Bremsdruck
/// beim Einlenken, Scheitelpunkt, Gasannahme, Lenkkorrekturen.
/// </summary>
/// <remarks>
/// <para>
/// Der <see cref="LapAnalyzer"/> beantwortet "wo geht Zeit verloren". Er sagt
/// dem Fahrer aber nicht, was seine Hände und Füße falsch machen: Zwei Fahrer
/// können in derselben Kurve dieselbe Zeit verlieren, der eine, weil er zu
/// früh bremst, der andere, weil er beim Einlenken noch voll auf der Bremse
/// steht. Diese Klasse liefert die zweite Antwort.
/// </para>
/// <para>
/// Zwei Sorten Grenzwert kommen zum Einsatz. Was fahrerisch <em>immer</em>
/// falsch ist – beide Pedale gleichzeitig, am Ausgang nachlenken – wird
/// absolut geprüft. Was nur im Vergleich Sinn ergibt – Einlenkpunkt, Gaspunkt –
/// wird gegen dieselbe Messung auf der Referenzrunde gehalten. Sonst würde
/// jede Haarnadel als "zu spät eingelenkt" gemeldet, bloß weil sie eine
/// Haarnadel ist.
/// </para>
/// </remarks>
public static class TechniqueAnalyzer
{
    /// <summary>Ab diesem Lenkeinschlag gilt die Kurve als angelenkt.</summary>
    private const float TurnInSteering = 0.12f;

    /// <summary>Darunter gilt das Lenkrad als gerade – nötig, um den Einlenkpunkt scharf zu machen.</summary>
    private const float ReleasedSteering = 0.06f;

    /// <summary>Ab hier gilt das Bremspedal als betätigt (wie im <see cref="CornerDetector"/>).</summary>
    private const float BrakeThreshold = 0.08f;

    /// <summary>Ab hier gilt das Gaspedal als betätigt – deutlich unter "voll am Gas".</summary>
    private const float ThrottleOnThreshold = 0.20f;

    /// <summary>Ab hier zählt gleichzeitiges Treten beider Pedale als Überschneidung.</summary>
    private const float OverlapThreshold = 0.15f;

    /// <summary>So weit vor der Bremszone beginnt die Suche nach dem Einlenkpunkt.</summary>
    private const float LeadInMetres = 40f;

    /// <summary>So weit hinter der Kurve endet die Messung.</summary>
    private const float RunOutMetres = 60f;

    /// <summary>Kleinere Lenkbewegungen sind Halten, keine Korrektur.</summary>
    private const float ReversalDeadband = 0.05f;

    /// <summary>Ab dieser Abweichung gilt der Einlenkpunkt als verschoben.</summary>
    private const float TurnInToleranceMetres = 10f;

    /// <summary>Ab dieser Abweichung gilt die Gasannahme als verschoben.</summary>
    private const float ThrottleToleranceMetres = 12f;

    /// <summary>Gasannahme so weit vor dem Lenk-Scheitelpunkt gilt immer als zu früh.</summary>
    private const float ThrottleBeforeApexMetres = 8f;

    /// <summary>So viele Meter mit beiden Pedalen sind zu viel.</summary>
    private const float PedalOverlapLimitMetres = 8f;

    /// <summary>Bremsdruck beim Einlenken, der unabhängig von der Referenz zu hoch ist.</summary>
    private const float HardBrakeAtTurnIn = 0.75f;

    /// <summary>Misst die Fahrweise durch eine Kurve.</summary>
    /// <param name="lap">Die auszuwertende Runde.</param>
    /// <param name="corner">Die Kurve, mit Grenzen aus der Referenzrunde.</param>
    /// <param name="referenceBinSize">Abschnittsbreite, auf die sich die Kurvengrenzen beziehen.</param>
    public static CornerTechnique Measure(RecordedLap lap, Corner corner, float referenceBinSize)
    {
        LapChannels channels = lap.Channels;
        if (channels.Count < 4 || lap.BinSize <= 0f)
        {
            return CornerTechnique.Empty;
        }

        float step = lap.BinSize;
        float cornerStart = corner.StartBin * referenceBinSize;
        float cornerEnd = corner.EndBin * referenceBinSize;
        float searchStart = MathF.Max(0f, (corner.BrakingStartBin * referenceBinSize) - LeadInMetres);
        float searchEnd = MathF.Min(lap.TrackLength, cornerEnd + RunOutMetres);

        if (cornerEnd <= cornerStart)
        {
            return CornerTechnique.Empty;
        }

        // Vorzeichen so drehen, dass "in die Kurve gelenkt" immer positiv ist.
        float sign = corner.Direction == CornerDirection.Right ? 1f : -1f;

        float turnIn = FindTurnIn(lap, sign, searchStart, cornerEnd, step);

        float lineApex = FindPeakSteering(lap, sign, turnIn, cornerEnd, step);
        (float speedApex, float apexPosition) = FindSpeedApex(lap, cornerStart, cornerEnd, step);

        float throttleOn = FindThrottlePickUp(lap, turnIn, searchEnd, step);

        return new CornerTechnique(
            TurnInDistance: turnIn,
            LineApexDistance: lineApex,
            BrakeAtTurnIn: Clamp01(RecordedLap.Sample(channels.Brake, step, turnIn)),
            TrailBrakeMetres: MeasureTrailBrake(lap, sign, turnIn, cornerEnd, step),
            ApexPosition: apexPosition,
            ThrottleVsApexMetres: float.IsNaN(throttleOn) ? 0f : throttleOn - lineApex,
            PedalOverlapMetres: MeasurePedalOverlap(lap, searchStart, searchEnd, step),
            SteeringReversals: CountReversals(lap, sign, turnIn, cornerEnd, step),
            SteeringAtApex: MathF.Abs(RecordedLap.Sample(channels.Steering, step, speedApex)),
            PeakSteeringAfterApex: MaxSteering(lap, speedApex, cornerEnd, step));
    }

    /// <summary>
    /// Erster Punkt, an dem das Lenkrad in Kurvenrichtung eingeschlagen wird.
    /// </summary>
    /// <remarks>
    /// Der Einlenkpunkt wird erst scharf, nachdem das Lenkrad einmal gerade war.
    /// Ohne diese Bedingung würde in einer Kurvenkombination der noch anstehende
    /// Einschlag der Vorkurve als Einlenkpunkt der Folgekurve durchgehen.
    /// Bleibt das Lenkrad durchgehend eingeschlagen, ist der Anfang des
    /// Suchfensters das Ergebnis – beide Runden werden gleich gemessen, der
    /// Vergleich stimmt also weiterhin.
    /// </remarks>
    private static float FindTurnIn(RecordedLap lap, float sign, float from, float to, float step)
    {
        float[] steering = lap.Channels.Steering;
        bool armed = false;

        for (float distance = from; distance <= to; distance += step)
        {
            float value = RecordedLap.Sample(steering, step, distance) * sign;

            if (!armed)
            {
                if (value < ReleasedSteering)
                {
                    armed = true;
                }

                continue;
            }

            if (value >= TurnInSteering)
            {
                return distance;
            }
        }

        return from;
    }

    /// <summary>
    /// Toleranz um den größten Lenkeinschlag, innerhalb derer noch "voll
    /// eingeschlagen" gilt.
    /// </summary>
    private const float SteeringPlateauBand = 0.02f;

    /// <summary>Wo der größte Lenkeinschlag liegt – der Scheitelpunkt der Linie.</summary>
    /// <remarks>
    /// In engen Kurven steht das Lenkrad über zig Meter am Anschlag; der Kanal
    /// läuft dann auf 1,0 und hat gar keine Spitze mehr. Deshalb wird die Mitte
    /// des voll eingeschlagenen Bereichs genommen und nicht dessen Anfang.
    /// </remarks>
    private static float FindPeakSteering(RecordedLap lap, float sign, float from, float to, float step)
    {
        float[] steering = lap.Channels.Steering;
        float peak = float.MinValue;

        for (float distance = from; distance <= to; distance += step)
        {
            peak = MathF.Max(peak, RecordedLap.Sample(steering, step, distance) * sign);
        }

        if (peak == float.MinValue)
        {
            return from;
        }

        return PlateauCentre(
            distance => RecordedLap.Sample(steering, step, distance) * sign >= peak - SteeringPlateauBand,
            from,
            to,
            step);
    }

    /// <summary>
    /// Mitte des Bereichs, in dem <paramref name="inPlateau"/> zutrifft.
    /// </summary>
    /// <remarks>
    /// Ein Messwert wie "langsamster Punkt" oder "größter Lenkeinschlag" ist in
    /// der Praxis fast nie ein Punkt, sondern ein Plateau. Den ersten Abschnitt
    /// des Plateaus zu nehmen, würde jede lange Kurve systematisch als "zu früh"
    /// bewerten.
    /// </remarks>
    private static float PlateauCentre(Func<float, bool> inPlateau, float from, float to, float step)
    {
        float first = float.NaN;
        float last = from;

        for (float distance = from; distance <= to; distance += step)
        {
            if (!inPlateau(distance))
            {
                continue;
            }

            if (float.IsNaN(first))
            {
                first = distance;
            }

            last = distance;
        }

        return float.IsNaN(first) ? from : (first + last) * 0.5f;
    }

    /// <summary>
    /// Toleranz um das Minimum, innerhalb derer das Tempo noch als "langsamster
    /// Punkt" gilt.
    /// </summary>
    private const float ApexPlateauMps = 0.4f;

    /// <summary>Langsamster Punkt der Kurve und seine relative Lage im Bogen.</summary>
    /// <remarks>
    /// Gesucht wird nicht der erste Abschnitt mit dem kleinsten Wert, sondern
    /// die Mitte des Bereichs, in dem das Tempo praktisch am Minimum liegt. In
    /// einer langgezogenen Kurve fährt man über zig Meter mit demselben Tempo;
    /// der erste dieser Abschnitte würde als Scheitelpunkt gemeldet, und jede
    /// noch so saubere Kurve bekäme den Befund "Scheitelpunkt zu früh".
    /// </remarks>
    private static (float Distance, float Position) FindSpeedApex(
        RecordedLap lap,
        float from,
        float to,
        float step)
    {
        float[] speed = lap.Channels.Speed;
        float slowest = float.MaxValue;

        for (float distance = from; distance <= to; distance += step)
        {
            slowest = MathF.Min(slowest, RecordedLap.Sample(speed, step, distance));
        }

        float limit = slowest + ApexPlateauMps;

        float at = PlateauCentre(
            distance => RecordedLap.Sample(speed, step, distance) <= limit,
            from,
            to,
            step);

        float position = Math.Clamp((at - from) / (to - from), 0f, 1f);
        return (at, position);
    }

    /// <summary>
    /// Erster Gasdruck nach dem Einlenken. Gesucht wird erst, nachdem der Fahrer
    /// einmal vom Gas war – sonst liefert eine Vollgaskurve einen Gaspunkt weit
    /// vor dem Scheitelpunkt und damit einen Fehler, den es nicht gibt.
    /// </summary>
    /// <returns>Distanz in Metern, oder <see cref="float.NaN"/>, wenn der Fahrer nie vom Gas war.</returns>
    private static float FindThrottlePickUp(RecordedLap lap, float from, float to, float step)
    {
        float[] throttle = lap.Channels.Throttle;
        bool lifted = false;

        for (float distance = from; distance <= to; distance += step)
        {
            float value = RecordedLap.Sample(throttle, step, distance);

            if (!lifted)
            {
                if (value < ThrottleOnThreshold)
                {
                    lifted = true;
                }

                continue;
            }

            if (value >= ThrottleOnThreshold)
            {
                return distance;
            }
        }

        return float.NaN;
    }

    /// <summary>Strecke, auf der gleichzeitig gebremst und gelenkt wurde.</summary>
    private static float MeasureTrailBrake(RecordedLap lap, float sign, float from, float to, float step)
    {
        LapChannels channels = lap.Channels;
        float metres = 0f;

        for (float distance = from; distance <= to; distance += step)
        {
            bool braking = RecordedLap.Sample(channels.Brake, step, distance) > BrakeThreshold;
            bool turned = RecordedLap.Sample(channels.Steering, step, distance) * sign >= TurnInSteering;

            if (braking && turned)
            {
                metres += step;
            }
        }

        return metres;
    }

    /// <summary>Strecke mit Gas und Bremse gleichzeitig.</summary>
    private static float MeasurePedalOverlap(RecordedLap lap, float from, float to, float step)
    {
        LapChannels channels = lap.Channels;
        float metres = 0f;

        for (float distance = from; distance <= to; distance += step)
        {
            bool throttle = RecordedLap.Sample(channels.Throttle, step, distance) > OverlapThreshold;
            bool brake = RecordedLap.Sample(channels.Brake, step, distance) > OverlapThreshold;

            if (throttle && brake)
            {
                metres += step;
            }
        }

        return metres;
    }

    /// <summary>
    /// Zählt Richtungswechsel der Lenkbewegung. Ein Totband filtert das Halten
    /// heraus, sonst zählt jedes Rauschen im Signal als Korrektur.
    /// </summary>
    private static int CountReversals(RecordedLap lap, float sign, float from, float to, float step)
    {
        float[] steering = lap.Channels.Steering;
        int reversals = 0;
        int direction = 0;
        float extreme = RecordedLap.Sample(steering, step, from) * sign;

        for (float distance = from + step; distance <= to; distance += step)
        {
            float value = RecordedLap.Sample(steering, step, distance) * sign;
            float change = value - extreme;

            if (direction == 0)
            {
                if (MathF.Abs(change) > ReversalDeadband)
                {
                    direction = MathF.Sign(change);
                    extreme = value;
                }

                continue;
            }

            if (MathF.Sign(change) == direction)
            {
                extreme = value;
            }
            else if (MathF.Abs(change) > ReversalDeadband)
            {
                reversals++;
                direction = -direction;
                extreme = value;
            }
        }

        return reversals;
    }

    /// <summary>Größter Betrag des Lenkeinschlags im Abschnitt.</summary>
    private static float MaxSteering(RecordedLap lap, float from, float to, float step)
    {
        float[] steering = lap.Channels.Steering;
        float peak = 0f;

        for (float distance = from; distance <= to; distance += step)
        {
            peak = MathF.Max(peak, MathF.Abs(RecordedLap.Sample(steering, step, distance)));
        }

        return peak;
    }

    /// <summary>
    /// Stellt die gefahrene Technik der Referenz gegenüber und benennt, was
    /// auffällt.
    /// </summary>
    public static TechniqueReport Compare(CornerTechnique driven, CornerTechnique reference, Corner corner)
    {
        float turnInDelta = driven.TurnInDistance - reference.TurnInDistance;
        TechniqueFlag flags = TechniqueFlag.None;

        if (turnInDelta < -TurnInToleranceMetres)
        {
            flags |= TechniqueFlag.TurnInTooEarly;
        }
        else if (turnInDelta > TurnInToleranceMetres)
        {
            flags |= TechniqueFlag.TurnInTooLate;
        }

        if (corner.HasBrakingZone)
        {
            // Hart auf der Bremse und schon am Einlenken: das Auto hat die
            // Vorderachse mit Bremsen belegt und kann nicht auch noch lenken.
            bool hardAbsolute = driven.BrakeAtTurnIn > HardBrakeAtTurnIn;
            bool harderThanReference =
                driven.BrakeAtTurnIn > 0.5f && driven.BrakeAtTurnIn > reference.BrakeAtTurnIn + 0.15f;

            if (hardAbsolute || harderThanReference)
            {
                flags |= TechniqueFlag.BrakingIntoTurnIn;
            }

            // Umgekehrter Fall: die Bremse wird vor dem Einlenken ganz gelöst.
            // Nur gegen die Referenz zu bewerten, weil es Kurven gibt, in die
            // man tatsächlich ohne Restbremse einlenkt.
            if (reference.TrailBrakeMetres > 15f && driven.TrailBrakeMetres < reference.TrailBrakeMetres * 0.4f)
            {
                flags |= TechniqueFlag.NoTrailBraking;
            }

            if (driven.ThrottleVsApexMetres < -ThrottleBeforeApexMetres)
            {
                flags |= TechniqueFlag.ThrottleTooEarly;
            }
            else if (driven.ThrottleVsApexMetres - reference.ThrottleVsApexMetres > ThrottleToleranceMetres)
            {
                flags |= TechniqueFlag.ThrottleTooLate;
            }
        }

        if (driven.ApexPosition < 0.33f ||
            (driven.ApexPosition < 0.5f && driven.ApexPosition < reference.ApexPosition - 0.10f))
        {
            flags |= TechniqueFlag.EarlyApex;
        }
        else if (driven.ApexPosition > 0.72f ||
                 (driven.ApexPosition > 0.5f && driven.ApexPosition > reference.ApexPosition + 0.10f))
        {
            flags |= TechniqueFlag.LateApex;
        }

        if (driven.PedalOverlapMetres > PedalOverlapLimitMetres)
        {
            flags |= TechniqueFlag.PedalOverlap;
        }

        if (driven.SteeringReversals >= 4 && driven.SteeringReversals >= reference.SteeringReversals + 3)
        {
            flags |= TechniqueFlag.RestlessSteering;
        }

        if (driven.AddsLockOnExit)
        {
            flags |= TechniqueFlag.AddingLockOnExit;
        }

        if (flags == TechniqueFlag.None)
        {
            return new TechniqueReport(driven, reference, flags, turnInDelta, string.Empty, string.Empty);
        }

        (string text, string speech) = Describe(flags, turnInDelta);
        return new TechniqueReport(driven, reference, flags, turnInDelta, text, speech);
    }

    /// <summary>
    /// Formuliert den Befund – ohne Kurvennamen, den setzt der Aufrufer davor.
    /// </summary>
    /// <remarks>
    /// Zusammenhängende Fehler werden als Kette beschrieben, weil der Fahrer
    /// sonst an der Wirkung statt an der Ursache arbeitet: Wer am Ausgang
    /// nachlenkt, muss nicht am Ausgang üben, sondern am Einlenkpunkt.
    /// </remarks>
    private static (string Text, string Speech) Describe(TechniqueFlag flags, float turnInDelta)
    {
        bool Has(TechniqueFlag flag) => (flags & flag) != 0;

        // Zwei Pedale gleichzeitig schlägt alles andere: solange das im Spiel
        // ist, sind sämtliche übrigen Messwerte Folgeerscheinungen.
        if (Has(TechniqueFlag.PedalOverlap))
        {
            return (
                "Gas und Bremse gleichzeitig – erst die Bremse ganz lösen, dann beschleunigen",
                "Nicht beide Pedale gleichzeitig.");
        }

        // Die klassische Anfängerkette: zu früh ans Lenkrad, dadurch zu früh an
        // der Innenseite, dadurch am Ausgang nachziehen müssen.
        if (Has(TechniqueFlag.TurnInTooEarly) &&
            (Has(TechniqueFlag.EarlyApex) || Has(TechniqueFlag.AddingLockOnExit)))
        {
            return (
                $"{-turnInDelta:0} m zu früh eingelenkt, dadurch Scheitelpunkt zu früh und am Ausgang nachgelenkt – länger geradeaus, dann entschlossener einlenken",
                "Später einlenken, dann geht das Lenkrad am Ausgang auf.");
        }

        if (Has(TechniqueFlag.BrakingIntoTurnIn) && Has(TechniqueFlag.TurnInTooEarly))
        {
            return (
                $"beim Einlenken steht noch voller Bremsdruck an, und das {-turnInDelta:0} m zu früh – erst den Druck abbauen, dann lenken",
                "Bremsdruck rausnehmen, bevor du einlenkst.");
        }

        if (Has(TechniqueFlag.BrakingIntoTurnIn))
        {
            return (
                "beim Einlenken liegt noch viel Bremsdruck an – die Vorderreifen können nicht bremsen und lenken zugleich",
                "Bremsdruck abbauen, während du einlenkst.");
        }

        if (Has(TechniqueFlag.ThrottleTooEarly))
        {
            return (
                "schon vor dem Scheitelpunkt am Gas, dadurch schiebt das Auto nach außen – erst das Lenkrad aufmachen, dann Gas",
                "Warte mit dem Gas bis zum Scheitelpunkt.");
        }

        if (Has(TechniqueFlag.TurnInTooEarly))
        {
            return (
                $"{-turnInDelta:0} m zu früh eingelenkt",
                "Etwas später einlenken.");
        }

        if (Has(TechniqueFlag.AddingLockOnExit))
        {
            return (
                "am Ausgang muss nachgelenkt werden – der Scheitelpunkt liegt zu weit innen",
                "Scheitelpunkt später anlegen.");
        }

        if (Has(TechniqueFlag.EarlyApex))
        {
            return (
                "Scheitelpunkt zu früh – du bist zu früh an der Innenseite und kannst am Ausgang nicht beschleunigen",
                "Den Scheitelpunkt später anlegen.");
        }

        if (Has(TechniqueFlag.TurnInTooLate))
        {
            return (
                $"{turnInDelta:0} m zu spät eingelenkt – der Bogen wird dadurch enger als nötig",
                "Etwas früher einlenken.");
        }

        if (Has(TechniqueFlag.NoTrailBraking))
        {
            return (
                "die Bremse ist vor dem Einlenken schon ganz gelöst – nimm etwas Restdruck mit, dann dreht das Auto besser ein",
                "Bremse weicher lösen, nicht schlagartig.");
        }

        if (Has(TechniqueFlag.LateApex))
        {
            return (
                "Scheitelpunkt zu spät – du verschenkst den halben Kurvenausgang",
                "Den Scheitelpunkt früher anlegen.");
        }

        if (Has(TechniqueFlag.ThrottleTooLate))
        {
            return (
                "nach dem Scheitelpunkt zu lange gewartet – ab dort gehört das Gaspedal aufgemacht",
                "Nach dem Scheitelpunkt sofort ans Gas.");
        }

        return (
            "unruhig am Lenkrad – mit einer Bewegung einlenken statt nachzukorrigieren",
            "Ruhiger am Lenkrad, eine Bewegung.");
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
