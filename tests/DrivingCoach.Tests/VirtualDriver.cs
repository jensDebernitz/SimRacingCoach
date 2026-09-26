using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

/// <summary>
/// Beschreibt, wie der virtuelle Fahrer eine einzelne Kurve anfährt.
/// </summary>
/// <param name="SpeedScale">
/// Faktor auf die mögliche Scheitelgeschwindigkeit; 1,0 ist die Referenz.
/// </param>
/// <param name="EntryShiftMetres">
/// Verschiebt den Beginn der langsamen Zone. Negativ heißt: der Fahrer ist zu
/// früh auf Kurventempo, bremst also zu früh. Positiv heißt: er bremst später.
/// </param>
/// <param name="ExitShiftMetres">
/// Verschiebt das Ende der langsamen Zone. Positiv heißt: der Fahrer hängt
/// hinter dem Scheitel noch im Langsamen fest, statt zu beschleunigen. Stark
/// negativ heißt: er ist schon vor dem Scheitelpunkt am Gas.
/// </param>
/// <param name="TurnInShiftMetres">
/// Verschiebt den Lenkeinschlag gegen die Streckengeometrie. Negativ heißt:
/// der Fahrer lenkt früher ein, als es die Kurve verlangt.
/// </param>
internal readonly record struct CornerStyle(
    float SpeedScale,
    float EntryShiftMetres = 0f,
    float ExitShiftMetres = 0f,
    float TurnInShiftMetres = 0f)
{
    /// <summary>
    /// Die Kurve wird wie in der Referenzrunde gefahren.
    /// </summary>
    /// <remarks>
    /// Bewusst als benanntes Feld statt als Standardwert im Konstruktor:
    /// <c>new CornerStyle()</c> würde den impliziten Struct-Konstruktor treffen
    /// und alle Felder auf null setzen – eine Kurve mit 0 m/s hält den
    /// virtuellen Fahrer für immer an.
    /// </remarks>
    internal static readonly CornerStyle Neutral = new(1f);
}

/// <summary>
/// Fährt synthetische Runden und schickt sie durch den echten
/// <see cref="LapRecorder"/>. Damit prüfen die Tests nicht nur die Auswertung,
/// sondern auch den Weg dorthin – Abtastung, Interpolation, Rundenerkennung.
/// </summary>
/// <remarks>
/// Fahrfehler werden nicht auf die Pedalkanäle gemalt, sondern über die
/// Tempolimits in das Geschwindigkeitsprofil gegeben. Der Solver aus dem
/// Simulator leitet daraus Bremspunkte, Pedalstellungen und Rundenzeit ab. Nur
/// so kostet ein zu früher Bremspunkt im Test auch wirklich Zeit – sonst würde
/// die Analyse gegen Daten geprüft, die es so nie geben kann.
/// </remarks>
internal static class VirtualDriver
{
    private const float UpdateHz = 90f;
    private const float WheelRadius = 0.33f;
    private const int GearCount = 6;

    internal static readonly SessionInfo Session = SessionFor(SimTrack.Default);

    /// <summary>
    /// Die Sitzungsdaten zu einer Strecke. Die Streckenlänge muss stimmen: Der
    /// <see cref="LapRecorder"/> legt daran seine Abschnitte fest und erkennt
    /// daran die Ziellinie.
    /// </summary>
    internal static SessionInfo SessionFor(SimTrack? track = null)
    {
        track ??= SimTrack.Default;

        return new SessionInfo(
            TrackName: track.Name,
            TrackVariation: "GP",
            TrackLength: track.Length,
            CarName: "Testwagen",
            CarClass: "Test",
            NumSectors: 3);
    }

    /// <summary>Eine perfekt gefahrene Runde: jede Kurve mit Faktor 1,0.</summary>
    internal static CornerStyle[] NeutralStyles(SimTrack? track = null)
    {
        track ??= SimTrack.Default;
        var styles = new CornerStyle[track.Corners.Count];
        Array.Fill(styles, CornerStyle.Neutral);
        return styles;
    }

    /// <summary>Erzeugt die Frames für <paramref name="laps"/> Runden am Stück.</summary>
    internal static List<TelemetryFrame> Frames(
        IReadOnlyList<CornerStyle> styles,
        int laps = 1,
        SimTrack? track = null)
    {
        track ??= SimTrack.Default;

        float[] profile = SpeedProfileSolver.Solve(
            BuildLimits(track, styles),
            SimTrack.StepMetres,
            SimTrack.MaxAcceleration,
            SimTrack.MaxDeceleration);

        float[] steeringShift = BuildSteeringShift(track, styles);

        // Bei 18 m/s – dem langsamsten Punkt der Teststrecke – braucht eine
        // Runde rund 21.000 Frames. Alles darüber heißt: das Auto steht, und
        // ohne diese Grenze würde die Schleife die Testsuite aufhängen.
        int maxFrames = (int)(track.Length / 0.2f) * laps + 1000;

        var frames = new List<TelemetryFrame>(8_000);

        float dt = 1f / UpdateHz;
        float distance = 0f;
        float lapTime = 0f;
        float lastLapTime = 0f;
        int lapsCompleted = 0;
        double timestamp = 0.0;

        while (lapsCompleted < laps)
        {
            Assert.True(
                frames.Count < maxFrames,
                $"Der virtuelle Fahrer kam nicht ins Ziel – steht bei {distance:0} m von {track.Length:0} m.");

            frames.Add(BuildFrame(track, profile, steeringShift, distance, lapTime, lastLapTime, lapsCompleted, timestamp));

            distance += SpeedAt(profile, track, distance) * dt;
            lapTime += dt;
            timestamp += dt;

            if (distance >= track.Length)
            {
                distance -= track.Length;
                lastLapTime = lapTime;
                lapTime = 0f;
                lapsCompleted++;
            }
        }

        // Ein erster Frame der Folgerunde, damit der Recorder die Ziellinie sieht.
        frames.Add(BuildFrame(track, profile, steeringShift, distance, 0f, lastLapTime, lapsCompleted, timestamp));

        return frames;
    }

    /// <summary>Fährt eine Runde und liefert das Ergebnis des Recorders.</summary>
    internal static LapCompleted DriveLap(IReadOnlyList<CornerStyle> styles, SimTrack? track = null)
    {
        var recorder = new LapRecorder();
        recorder.ResetSession(SessionFor(track));

        LapCompleted? result = null;
        recorder.LapFinished += completed => result ??= completed;

        foreach (TelemetryFrame frame in Frames(styles, laps: 1, track))
        {
            recorder.Accept(in frame);
        }

        Assert.True(result.HasValue, "Der Recorder hat keine Runde abgeschlossen.");
        return result.Value;
    }

    /// <summary>
    /// Fährt mehrere Runden am Stück. Jede liegt etwas anders auf der Strecke –
    /// genau das braucht die Streckenkarte, um eine Breite zu lernen.
    /// </summary>
    internal static List<RecordedLap> DriveLaps(int laps, IReadOnlyList<CornerStyle>? styles = null, SimTrack? track = null)
    {
        var recorder = new LapRecorder();
        recorder.ResetSession(SessionFor(track));

        var valid = new List<RecordedLap>();
        recorder.LapFinished += completed =>
        {
            if (completed.IsValid)
            {
                valid.Add(completed.Lap);
            }
        };

        foreach (TelemetryFrame frame in Frames(styles ?? NeutralStyles(track), laps, track))
        {
            recorder.Accept(in frame);
        }

        Assert.Equal(laps, valid.Count);
        return valid;
    }

    /// <summary>Fährt eine gültige Referenzrunde und gibt sie zurück.</summary>
    internal static RecordedLap DriveReferenceLap(SimTrack? track = null)
    {
        LapCompleted completed = DriveLap(NeutralStyles(track), track);
        Assert.True(completed.IsValid, completed.InvalidReason);
        return completed.Lap;
    }

    /// <summary>
    /// Tempolimit je Streckenabschnitt, inklusive der Fahrfehler aus
    /// <paramref name="styles"/>.
    /// </summary>
    private static float[] BuildLimits(SimTrack track, IReadOnlyList<CornerStyle> styles)
    {
        var limits = new float[track.StepCount];
        Array.Fill(limits, track.TopSpeed);

        for (int c = 0; c < track.Corners.Count; c++)
        {
            SimCorner corner = track.Corners[c];
            CornerStyle style = c < styles.Count ? styles[c] : CornerStyle.Neutral;

            float speed = corner.ApexSpeed * style.SpeedScale;
            float from = corner.EntryDistance + style.EntryShiftMetres;
            float to = corner.EntryDistance + corner.Length + style.ExitShiftMetres;

            int fromIndex = (int)(from / SimTrack.StepMetres);
            int toIndex = (int)(to / SimTrack.StepMetres);

            for (int i = fromIndex; i <= toIndex; i++)
            {
                int index = track.Wrap(i);
                limits[index] = MathF.Min(limits[index], speed);
            }
        }

        return limits;
    }

    /// <summary>
    /// Verschiebung des Lenkeinschlags gegen die Streckengeometrie, je
    /// Streckenabschnitt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ein Einlenkfehler lässt sich – anders als ein Bremsfehler – nicht über
    /// das Geschwindigkeitsprofil ausdrücken: Wer zu früh einlenkt, fährt eine
    /// andere Linie, nicht ein anderes Tempo. Deshalb wird hier ausnahmsweise
    /// direkt am Kanal gedreht.
    /// </para>
    /// <para>
    /// Verschoben wird nur <see cref="TelemetryFrame.Steering"/>, nicht die
    /// Gierrate. Das ist kein Schummeln, sondern deckt sich damit, was die
    /// Auswertung liest: Die Kurvengrenzen kommen immer aus der Referenzrunde,
    /// und die Technikmessung wertet an der gefahrenen Runde ausschließlich
    /// Lenkung, Pedale und Tempo aus.
    /// </para>
    /// </remarks>
    private static float[] BuildSteeringShift(SimTrack track, IReadOnlyList<CornerStyle> styles)
    {
        var shift = new float[track.StepCount];

        for (int c = 0; c < track.Corners.Count; c++)
        {
            CornerStyle style = c < styles.Count ? styles[c] : CornerStyle.Neutral;
            if (style.TurnInShiftMetres == 0f)
            {
                continue;
            }

            SimCorner corner = track.Corners[c];

            // Der Rand des Bereichs muss weiter außen liegen als die
            // Verschiebung selbst, sonst entsteht an der Kante ein Sprung im
            // Lenkkanal – und den würde die Auswertung als Lenkkorrektur zählen.
            float margin = MathF.Abs(style.TurnInShiftMetres) + 20f;
            float from = corner.EntryDistance - margin;
            float to = corner.EntryDistance + corner.Length + margin;

            for (int i = (int)(from / SimTrack.StepMetres); i <= (int)(to / SimTrack.StepMetres); i++)
            {
                shift[track.Wrap(i)] = style.TurnInShiftMetres;
            }
        }

        return shift;
    }

    private static TelemetryFrame BuildFrame(
        SimTrack track,
        float[] profile,
        float[] steeringShift,
        float distance,
        float lapTime,
        float lastLapTime,
        int lapsCompleted,
        double timestamp)
    {
        float speed = SpeedAt(profile, track, distance);
        float ahead = SpeedAt(profile, track, distance + SimTrack.StepMetres);
        float behind = SpeedAt(profile, track, distance - SimTrack.StepMetres);

        // Längsbeschleunigung aus dem Profil: dv/dt = v · dv/ds
        float dvds = (ahead - behind) / (2f * SimTrack.StepMetres);
        float longitudinal = speed * dvds;

        float throttle = longitudinal > 0f ? MathF.Min(1f, longitudinal / SimTrack.MaxAcceleration) : 0f;
        float brake = longitudinal < 0f ? MathF.Min(1f, -longitudinal / SimTrack.MaxDeceleration) : 0f;

        float curvature = track.CurvatureAt(distance);

        // Die Lenkung darf der Geometrie vorauseilen oder hinterherhinken; die
        // Gierrate folgt weiter der Strecke.
        float shift = steeringShift[track.Wrap((int)(distance / SimTrack.StepMetres))];
        float steeringCurvature = shift == 0f ? curvature : track.CurvatureAt(Wrap(track, distance - shift));

        int gear = Math.Clamp(1 + (int)(speed / (track.TopSpeed / GearCount)), 1, GearCount);
        float wheelRps = speed / (2f * MathF.PI * WheelRadius);

        return new TelemetryFrame
        {
            Timestamp = timestamp,
            GameState = GameState.InGamePlaying,
            SessionState = SessionState.Practice,
            RaceState = RaceState.Racing,
            PitMode = PitMode.None,
            LapDistance = distance,

            // Die Linie ist jede Runde etwas anders. Erst dadurch hat der
            // gelernte Streckenkorridor überhaupt eine Breite.
            WorldPosition = track.PointAt(
                distance,
                track.RacingOffsetAt(distance, 1f + (MathF.Sin(lapsCompleted * 2.4f) * 0.25f))),
            Pose = new CarPose(track.HeadingAt(distance), 0f, 0f),
            CurrentLapTime = lapTime,
            LastLapTime = lastLapTime,
            BestLapTime = lastLapTime,
            LapsCompleted = lapsCompleted,
            CurrentLap = lapsCompleted + 1,
            CurrentSector = 0,
            LapInvalidated = false,
            Speed = speed,
            Rpm = 4000f,
            MaxRpm = 7500f,
            Gear = gear,
            NumGears = GearCount,
            Throttle = throttle,
            Brake = brake,
            Clutch = 0f,
            Steering = Math.Clamp(steeringCurvature * 220f, -1f, 1f),
            YawRate = curvature * speed,
            LateralAcceleration = curvature * speed * speed,
            LongitudinalAcceleration = longitudinal,
            TyreRps = new Wheels4(wheelRps, wheelRps, wheelRps, wheelRps),
            Terrain = new TerrainSet(Terrain.Road, Terrain.Road, Terrain.Road, Terrain.Road),
            AbsActive = false,
            CarFlags = CarFlags.EngineActive,
            BrakeBias = 0.56f,
        };
    }

    /// <summary>Distanz zyklisch auf die Rundenlänge abbilden.</summary>
    private static float Wrap(SimTrack track, float distance)
    {
        float wrapped = distance % track.Length;
        return wrapped < 0f ? wrapped + track.Length : wrapped;
    }

    private static float SpeedAt(float[] profile, SimTrack track, float distance)
    {
        float position = distance / SimTrack.StepMetres;
        int index = (int)MathF.Floor(position);
        float fraction = position - index;

        float a = profile[track.Wrap(index)];
        float b = profile[track.Wrap(index + 1)];
        return a + (b - a) * fraction;
    }
}
