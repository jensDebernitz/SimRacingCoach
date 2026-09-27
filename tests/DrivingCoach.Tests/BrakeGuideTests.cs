using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft den Bremspunkt: wo er liegt und wann der Countdown darüber erscheint.
/// </summary>
public sealed class BrakeGuideTests
{
    private static readonly RecordedLap Reference = VirtualDriver.DriveReferenceLap();

    private static readonly IReadOnlyList<Corner> Corners = new CornerDetector().Detect(Reference);

    private static readonly IdealLine Line = BuildLine();

    [Fact]
    public void Ohne_Referenzrunde_gibt_es_keinen_Bremspunkt()
    {
        Assert.Null(BrakeGuide.BrakePointOf(BrakingCorner(), reference: null, line: null));
    }

    /// <summary>
    /// Ohne Ideallinie bleibt nur die Referenzrunde – dort steht der Bremspunkt
    /// als Abschnitts-Index und muss in Meter umgerechnet werden.
    /// </summary>
    [Fact]
    public void Ohne_Ideallinie_kommt_der_Bremspunkt_aus_der_Referenz()
    {
        Corner corner = BrakingCorner();

        Assert.Equal(
            corner.BrakingStartBin * Reference.BinSize,
            BrakeGuide.BrakePointOf(corner, Reference, line: null));
    }

    /// <summary>
    /// Eine Kurve, die ohne Bremsen geht, hat keinen Bremspunkt – und darf sich
    /// auch keinen aus dem Index 0 erfinden, den der Kurvenerkenner einsetzt,
    /// wenn er keine Bremsphase findet.
    /// </summary>
    [Fact]
    public void Ohne_Bremsphase_gibt_es_nichts_anzusagen()
    {
        Corner corner = BrakingCorner() with { BrakingStartBin = 0, StartBin = 0 };

        Assert.False(corner.HasBrakingZone);
        Assert.Null(BrakeGuide.BrakePointOf(corner, Reference, line: null));
    }

    /// <summary>
    /// Der Bremspunkt liegt vor dem Scheitel, nicht dahinter. Klingt
    /// selbstverständlich, ist es aber nicht: Die Rückwärtssuche auf der
    /// Ideallinie läuft über Start/Ziel hinaus, und ein Vorzeichenfehler dort
    /// ergäbe einen Punkt eine ganze Runde entfernt.
    /// </summary>
    [Fact]
    public void Mit_Ideallinie_liegt_der_Bremspunkt_vor_dem_Scheitel()
    {
        Corner corner = BrakingCorner();
        float? point = BrakeGuide.BrakePointOf(corner, Reference, Line);

        Assert.True(point.HasValue, $"{corner.Name} bekommt mit Ideallinie keinen Bremspunkt.");

        float apex = corner.ApexBin * Reference.BinSize;
        float before = BrakeGuide.MetresTo(apex, point!.Value, Reference.TrackLength);

        Assert.InRange(before, 15f, 400f);
    }

    [Fact]
    public void Jede_Kurve_bekommt_einen_Platz_in_der_Liste()
    {
        float[] points = BrakeGuide.BrakePoints(Corners, Reference, Line);

        Assert.Equal(Corners.Count, points.Length);

        // Ohne mindestens einen echten Bremspunkt wäre die ganze Anzeige tot.
        Assert.Contains(points, p => !float.IsNaN(p));
    }

    /// <summary>
    /// Der Countdown zählt in Fahrtrichtung. Ein Bremspunkt kurz hinter der
    /// Ziellinie ist von kurz davor aus wenige Meter entfernt, nicht eine
    /// Runde minus wenige Meter.
    /// </summary>
    [Fact]
    public void Der_Abstand_rechnet_ueber_Start_Ziel_hinweg()
    {
        const float length = 4000f;

        Assert.Equal(100f, BrakeGuide.MetresTo(target: 50f, lapDistance: 3950f, length), precision: 3);
        Assert.Equal(200f, BrakeGuide.MetresTo(target: 1200f, lapDistance: 1000f, length), precision: 3);
        Assert.Equal(0f, BrakeGuide.MetresTo(target: 1000f, lapDistance: 1000f, length), precision: 3);
    }

    [Fact]
    public void Ausserhalb_der_Strecke_kommt_kein_Countdown()
    {
        CoachState state = Approaching(out float point);

        Assert.Null(BrakeGuide.CueFor(state, Frame(point - 60f, speed: 60f, driving: false)));
    }

    [Fact]
    public void Ohne_Bremspunkt_kommt_kein_Countdown()
    {
        CoachState state = Approaching(out float point);
        state.UpcomingBrakePoint = null;

        Assert.Null(BrakeGuide.CueFor(state, Frame(point - 60f, speed: 60f)));
    }

    /// <summary>
    /// Der Countdown erscheint erst in Reichweite. Stünde er die ganze Runde
    /// über da, liefe der Balken fast immer leer mit und sagte nichts aus.
    /// </summary>
    [Fact]
    public void Der_Countdown_erscheint_erst_in_Reichweite()
    {
        CoachState state = Approaching(out float point);

        // Bei 60 m/s reicht das Fenster 210 m weit (3,5 s).
        Assert.Null(BrakeGuide.CueFor(state, Frame(point - 400f, speed: 60f)));
        Assert.NotNull(BrakeGuide.CueFor(state, Frame(point - 150f, speed: 60f)));
    }

    /// <summary>
    /// Langsam ist das Fenster in Metern kleiner, aber nie kürzer als 70 m –
    /// sonst blitzte der Countdown in der Schikane nur kurz auf.
    /// </summary>
    [Fact]
    public void Langsam_bleibt_das_Fenster_lesbar_lang()
    {
        CoachState state = Approaching(out float point);

        Assert.NotNull(BrakeGuide.CueFor(state, Frame(point - 60f, speed: 10f)));
    }

    [Fact]
    public void Der_Balken_fuellt_sich_bis_zum_Bremspunkt()
    {
        CoachState state = Approaching(out float point);

        BrakeCue far = Cue(state, point - 200f, speed: 60f);
        BrakeCue near = Cue(state, point - 40f, speed: 60f);
        BrakeCue here = Cue(state, point, speed: 60f);

        Assert.True(far.Progress < near.Progress, $"{far.Progress:0.00} steht nicht vor {near.Progress:0.00}.");
        Assert.True(near.Progress < here.Progress, $"{near.Progress:0.00} steht nicht vor {here.Progress:0.00}.");
        Assert.Equal(1.0, here.Progress, precision: 3);
        Assert.InRange(far.Progress, 0.0, 1.0);
    }

    [Fact]
    public void Am_Bremspunkt_steht_bremsen_und_nicht_die_Entfernung()
    {
        CoachState state = Approaching(out float point);

        BrakeCue here = Cue(state, point, speed: 60f);

        Assert.True(here.IsNow);
        Assert.Contains("BREMSEN", here.Text);
        Assert.Contains(here.CornerName, here.Text);
    }

    [Fact]
    public void Vorher_nennt_der_Countdown_die_Entfernung()
    {
        CoachState state = Approaching(out float point);

        BrakeCue soon = Cue(state, point - 150f, speed: 60f);

        Assert.False(soon.IsNow);
        Assert.Contains("150 m", soon.Text);
        Assert.DoesNotContain("BREMSEN", soon.Text);
    }

    /// <summary>
    /// Ein Bremspunkt kurz hinter Start/Ziel muss den Countdown auch schon vor
    /// der Linie auslösen – gerade dort steht auf dieser Strecke oft die erste
    /// Kurve.
    /// </summary>
    [Fact]
    public void Der_Countdown_ueberlebt_die_Ziellinie()
    {
        CoachState state = Approaching(out _);
        state.UpcomingBrakePoint = 40f;

        BrakeCue cue = Cue(state, Reference.TrackLength - 60f, speed: 60f);

        Assert.Equal(100f, cue.MetresAway, precision: 0);
    }

    /// <summary>
    /// Die Bremskraft steht schon im Countdown und nicht erst beim Tritt: Wer
    /// weiß, dass gleich voll gebremst wird, stellt den Fuß anders auf.
    /// </summary>
    [Fact]
    public void Der_Countdown_nennt_die_Bremskraft()
    {
        CoachState state = Approaching(out float point);
        state.UpcomingBrakePlan = SamplePlan with { Peak = 0.8f };

        Assert.Contains("80 %", Cue(state, point - 100f, speed: 60f).Text);
        Assert.Contains("80 %", Cue(state, point, speed: 60f).Text);
    }

    /// <summary>
    /// Ohne bekannte Bremskraft bleibt der Countdown, wie er war – lieber keine
    /// Zahl als eine geratene.
    /// </summary>
    [Fact]
    public void Ohne_Vorgabe_nennt_der_Countdown_keine_Kraft()
    {
        CoachState state = Approaching(out float point);

        Assert.DoesNotContain("%", Cue(state, point - 100f, speed: 60f).Text);
    }

    [Fact]
    public void Die_Bremszone_reicht_vom_Bremspunkt_bis_zum_Ende_der_Vorgabe()
    {
        Assert.Null(Zone(lapDistance: 999f));
        Assert.NotNull(Zone(lapDistance: 1000f));
        Assert.NotNull(Zone(lapDistance: 1149f));
        Assert.Null(Zone(lapDistance: 1151f));
    }

    [Fact]
    public void Der_Restweg_der_Bremszone_zaehlt_herunter()
    {
        Assert.Equal(150f, Zone(1000f)!.Value.MetresLeft, precision: 3);
        Assert.Equal(90f, Zone(1060f)!.Value.MetresLeft, precision: 3);
        Assert.Equal(0f, Zone(1150f)!.Value.MetresLeft, precision: 3);
    }

    /// <summary>
    /// Eine Bremszone, die über Start/Ziel läuft, darf nicht an der Linie
    /// abreißen – die erste Kurve liegt auf vielen Strecken genau dort.
    /// </summary>
    [Fact]
    public void Die_Bremszone_ueberlebt_die_Ziellinie()
    {
        ActiveBraking? zone = BrakeGuide.ActiveZone(
            [BrakingCorner()], [3950f], [SamplePlan], lapDistance: 30f, trackLength: 4000f);

        Assert.True(zone.HasValue, "Die Bremszone endet an der Ziellinie.");
        Assert.Equal(70f, zone!.Value.MetresLeft, precision: 3);
    }

    [Fact]
    public void Ohne_Vorgabe_gibt_es_keine_Bremszone()
    {
        Assert.Null(BrakeGuide.ActiveZone(
            [BrakingCorner()], [1000f], [null], lapDistance: 1050f, trackLength: 4000f));
    }

    [Fact]
    public void In_der_Bremszone_steht_Soll_gegen_Ist()
    {
        BrakeForceCue cue = Force(brake: 0.6f);

        Assert.Contains(cue.CornerName, cue.Text);
        Assert.Contains("Bremse 60 %", cue.Text);
        Assert.Contains("Soll", cue.Text);
        Assert.Equal(0.6, cue.Progress, precision: 3);
    }

    /// <summary>
    /// Der Sollwert kommt laufend aus der Referenzrunde, nicht aus den zwei
    /// Stufen der Vorgabe: Die Stufen sind zum Sagen da, die Anzeige darf der
    /// gemessenen Kurve folgen.
    /// </summary>
    [Fact]
    public void Der_Sollwert_stammt_aus_der_Referenzrunde()
    {
        CoachState state = Braking(out float distance);
        PedalTarget? target = PedalGuide.TargetAt(Reference, distance);

        Assert.True(target.HasValue, "Die Referenzrunde gibt an dieser Stelle nichts her.");
        Assert.Equal(target!.Value.Brake, Force(brake: 0f).Target, precision: 3);
    }

    [Fact]
    public void Ein_zu_weicher_Tritt_faellt_auf()
    {
        BrakeForceCue matched = Force(brake: Force(brake: 0f).Target);
        BrakeForceCue soft = Force(brake: MathF.Max(0f, matched.Target - 0.3f));

        Assert.False(matched.IsOff, $"Soll {matched.TargetPercent} % getroffen und trotzdem angemahnt.");
        Assert.True(soft.IsOff, $"{soft.ActualPercent} % statt {soft.TargetPercent} % fällt nicht auf.");
    }

    /// <summary>
    /// Der Lösepunkt zählt herunter und verschwindet, sobald er hinter dem Auto
    /// liegt – ab da steht die neue Kraft ohnehin als Sollwert da.
    /// </summary>
    [Fact]
    public void Der_Loesepunkt_zaehlt_herunter_und_verschwindet_dann()
    {
        CoachState state = Braking(out float distance);

        // Vorgabe: Zone 150 m, Lösen 60 m vor deren Ende.
        state.Braking = new ActiveBraking(BrakingCorner(), SamplePlan, MetresLeft: 100f);
        BrakeForceCue ahead = Force(state, distance, brake: 1f);

        state.Braking = new ActiveBraking(BrakingCorner(), SamplePlan, MetresLeft: 40f);
        BrakeForceCue passed = Force(state, distance, brake: 1f);

        Assert.True(ahead.HasRelease);
        Assert.Equal(40f, ahead.ReleaseInMetres, precision: 3);
        Assert.Contains("auf 70 %", ahead.Text);

        Assert.False(passed.HasRelease);
        Assert.DoesNotContain("auf 70 %", passed.Text);
    }

    [Fact]
    public void Ausserhalb_der_Strecke_gibt_es_keine_Kraftanzeige()
    {
        CoachState state = Braking(out float distance);

        Assert.Null(BrakeGuide.ForceCueFor(state, Frame(distance, speed: 40f, driving: false)));
    }

    [Fact]
    public void Ohne_Bremszone_gibt_es_keine_Kraftanzeige()
    {
        CoachState state = Braking(out float distance);
        state.Braking = null;

        Assert.Null(BrakeGuide.ForceCueFor(state, Frame(distance, speed: 40f)));
    }

    /// <summary>Eine Vorgabe mit klaren Zahlen, an denen sich rechnen lässt.</summary>
    private static readonly BrakePlan SamplePlan = new(
        Peak: 1f, ReleaseTo: 0.7f, ReleaseAtMetres: 60f, EndMetres: 150f);

    private static ActiveBraking? Zone(float lapDistance) => BrakeGuide.ActiveZone(
        [BrakingCorner()], [1000f], [SamplePlan], lapDistance, trackLength: 4000f);

    private static BrakeForceCue Force(float brake)
    {
        CoachState state = Braking(out float distance);
        return Force(state, distance, brake);
    }

    private static BrakeForceCue Force(CoachState state, float lapDistance, float brake)
    {
        BrakeForceCue? cue = BrakeGuide.ForceCueFor(
            state, Frame(lapDistance, speed: 40f) with { Brake = brake });

        Assert.True(cue.HasValue, $"Bei {lapDistance:0} m kommt keine Kraftanzeige.");
        return cue!.Value;
    }

    /// <summary>
    /// Ein Zustand mitten in der Bremsphase – das, was <see cref="CoachEngine"/>
    /// ab dem Bremspunkt hineinschreibt.
    /// </summary>
    private static CoachState Braking(out float lapDistance)
    {
        Corner corner = BrakingCorner();
        float point = corner.BrakingStartBin * Reference.BinSize;

        // Ein Stück in die Zone hinein: Am Bremspunkt selbst baut sich der
        // Druck in der Referenz gerade erst auf.
        lapDistance = point + 20f;

        return new CoachState
        {
            Session = VirtualDriver.Session,
            Reference = Reference,
            Corners = Corners,
            Braking = new ActiveBraking(corner, SamplePlan, MetresLeft: 100f),
        };
    }

    private static BrakeCue Cue(CoachState state, float lapDistance, float speed)
    {
        BrakeCue? cue = BrakeGuide.CueFor(state, Frame(lapDistance, speed));
        Assert.True(cue.HasValue, $"Bei {lapDistance:0} m kommt kein Countdown.");
        return cue!.Value;
    }

    /// <summary>
    /// Ein Zustand, in dem eine Kurve mit Bremspunkt unmittelbar bevorsteht –
    /// genau das, was <see cref="CoachEngine"/> im Fahrbetrieb hineinschreibt.
    /// </summary>
    private static CoachState Approaching(out float brakePoint)
    {
        Corner corner = BrakingCorner();
        brakePoint = corner.BrakingStartBin * Reference.BinSize;

        return new CoachState
        {
            Session = VirtualDriver.Session,
            Reference = Reference,
            Corners = Corners,
            UpcomingCorner = corner,
            UpcomingBrakePoint = brakePoint,
        };
    }

    private static TelemetryFrame Frame(float lapDistance, float speed, bool driving = true) =>
        new()
        {
            GameState = driving ? GameState.InGamePlaying : GameState.InGameInMenuTimeTicking,
            LapDistance = Wrap(lapDistance),
            Speed = speed,
        };

    private static float Wrap(float distance)
    {
        float wrapped = distance % Reference.TrackLength;
        return wrapped < 0f ? wrapped + Reference.TrackLength : wrapped;
    }

    /// <summary>
    /// Eine Kurve, vor der in der Referenz gebremst wurde, und die weit genug
    /// hinter der Ziellinie liegt, dass sich davor Distanzen abmessen lassen.
    /// </summary>
    private static Corner BrakingCorner()
    {
        Assert.NotEmpty(Corners);

        Corner? corner = Corners.FirstOrDefault(
            c => c.HasBrakingZone && c.BrakingStartBin * Reference.BinSize > 500f);

        Assert.True(corner is not null, "Keine der erkannten Kurven hat eine brauchbare Bremsphase.");
        return corner!;
    }

    /// <summary>
    /// Die gerechnete Ideallinie zur Teststrecke. Vier Runden, weil die
    /// Streckenkarte erst aus mehreren leicht verschiedenen Linien eine Breite
    /// lernt.
    /// </summary>
    private static IdealLine BuildLine()
    {
        List<RecordedLap> laps = VirtualDriver.DriveLaps(4);
        RecordedLap first = laps[0];

        TrackMap map = TrackMap.Empty(
            VirtualDriver.Session.TrackKey,
            VirtualDriver.Session.TrackDisplayName,
            first.TrackLength,
            first.BinSize,
            first.Channels.Count);

        foreach (RecordedLap lap in laps)
        {
            Assert.True(map.Learn(lap), "Die Karte hat eine Runde abgelehnt, die zu ihr passen müsste.");
        }

        IdealLine? line = IdealLineSolver.Solve(map, GripEstimate.Default, topSpeed: 78f);
        Assert.True(line is not null, "Aus vier Runden kam keine Ideallinie heraus.");
        return line!;
    }
}
