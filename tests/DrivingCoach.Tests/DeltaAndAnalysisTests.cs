using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Tests;

public class DeltaEngineTests
{
    [Fact]
    public void Ohne_Referenz_gibt_es_kein_Delta()
    {
        var engine = new DeltaEngine();
        List<TelemetryFrame> frames = VirtualDriver.Frames(VirtualDriver.NeutralStyles());

        DeltaState state = engine.Update(frames[500]);

        Assert.False(state.HasReference);
        Assert.False(state.IsMeaningful);
    }

    [Fact]
    public void Gegen_die_eigene_Runde_bleibt_das_Delta_bei_null()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();

        var engine = new DeltaEngine();
        engine.SetReference(reference);

        float worst = 0f;
        foreach (TelemetryFrame frame in VirtualDriver.Frames(VirtualDriver.NeutralStyles()))
        {
            DeltaState state = engine.Update(in frame);
            if (state.IsMeaningful)
            {
                worst = MathF.Max(worst, MathF.Abs(state.Delta));
            }
        }

        // Rein rechnerisch müsste das Delta exakt null sein; die Restabweichung
        // stammt allein aus der Interpolation auf die 5-m-Abschnitte.
        Assert.True(worst < 0.05f, $"Größte Abweichung gegen die eigene Runde: {worst:0.000} s.");
    }

    [Fact]
    public void Langsamere_Runde_erzeugt_wachsendes_positives_Delta()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();

        var slow = VirtualDriver.NeutralStyles();
        Array.Fill(slow, new CornerStyle(SpeedScale: 0.88f));

        var engine = new DeltaEngine();
        engine.SetReference(reference);

        float atHalfway = 0f;
        float atFinish = 0f;

        foreach (TelemetryFrame frame in VirtualDriver.Frames(slow))
        {
            DeltaState state = engine.Update(in frame);
            if (!state.IsMeaningful)
            {
                continue;
            }

            if (frame.LapDistance < reference.TrackLength * 0.5f)
            {
                atHalfway = state.Delta;
            }
            else if (frame.LapsCompleted == 0)
            {
                atFinish = state.Delta;
            }
        }

        Assert.True(atHalfway > 0f, $"Delta zur Hälfte der Runde: {atHalfway:0.000} s.");
        Assert.True(atFinish > atHalfway, $"Delta wuchs nicht: {atHalfway:0.000} s → {atFinish:0.000} s.");
    }

    [Fact]
    public void Vorhergesagte_Rundenzeit_folgt_dem_Delta()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();

        var engine = new DeltaEngine();
        engine.SetReference(reference);

        List<TelemetryFrame> frames = VirtualDriver.Frames(VirtualDriver.NeutralStyles());
        DeltaState state = engine.Update(frames[^2]);

        Assert.Equal(reference.LapTime, state.ReferenceLapTime);
        Assert.Equal(state.ReferenceLapTime + state.Delta, state.PredictedLapTime, 3);
    }
}

public class LapAnalyzerTests
{
    /// <summary>
    /// Fährt eine Runde, in der genau eine Kurve anders angefahren wird, und
    /// liefert den Hinweis zu eben dieser Kurve.
    /// </summary>
    private static (CornerAdvice Advice, LapAnalysis Analysis) AnalyzeSingleCornerMistake(
        int cornerIndex,
        CornerStyle mistake)
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        var styles = VirtualDriver.NeutralStyles();
        styles[cornerIndex] = mistake;

        LapCompleted lap = VirtualDriver.DriveLap(styles);
        Assert.True(lap.IsValid, lap.InvalidReason);

        LapAnalysis analysis = new LapAnalyzer().Analyze(lap.Lap, reference, corners);
        return (analysis.Corners[cornerIndex], analysis);
    }

    [Fact]
    public void Identische_Runde_ergibt_keinen_einzigen_Hinweis()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        LapAnalysis analysis = new LapAnalyzer().Analyze(reference, reference, corners);

        Assert.Empty(analysis.WorstFirst);
        Assert.All(analysis.Corners, advice => Assert.Equal(AdviceKind.Neutral, advice.Kind));
        Assert.Equal(0f, analysis.LapDelta);
    }

    [Fact]
    public void Erkennt_zu_fruehes_Anbremsen()
    {
        // Der Fahrer ist 60 m zu früh auf Kurventempo – der klassische
        // "sicherheitshalber schon mal bremsen"-Fehler.
        (CornerAdvice advice, _) = AnalyzeSingleCornerMistake(
            cornerIndex: 0,
            new CornerStyle(SpeedScale: 1f, EntryShiftMetres: -60f));

        Assert.Equal(AdviceKind.BrakeLater, advice.Kind);
        Assert.True(advice.BrakePointDelta < -20f, $"Bremspunkt-Delta {advice.BrakePointDelta:0} m.");
        Assert.True(advice.TimeLost > 0.05f, $"Zeitverlust {advice.TimeLost:0.000} s.");
        Assert.Contains("später bremsen", advice.Text);
        Assert.Equal("Kurve 1", advice.Corner.Name);
    }

    [Fact]
    public void Erkennt_zu_wenig_Tempo_am_Scheitelpunkt()
    {
        // Gleicher Bremspunkt, aber am Scheitel zu langsam.
        (CornerAdvice advice, _) = AnalyzeSingleCornerMistake(
            cornerIndex: 2,
            new CornerStyle(SpeedScale: 0.85f));

        Assert.Equal(AdviceKind.MoreApexSpeed, advice.Kind);
        Assert.True(advice.ApexSpeedDelta < -1f, $"Scheiteltempo-Delta {advice.ApexSpeedDelta:0.0} m/s.");
        Assert.Contains("km/h mehr am Scheitel", advice.Text);
    }

    [Fact]
    public void Erkennt_Ueberfahren_der_Kurve()
    {
        // Zu spät angebremst, dadurch den Scheitel verpasst und erst 40 m
        // später als nötig wieder frei – der Fehler, der Anfänger am meisten
        // Zeit kostet.
        (CornerAdvice advice, _) = AnalyzeSingleCornerMistake(
            cornerIndex: 3,
            new CornerStyle(SpeedScale: 0.82f, EntryShiftMetres: 40f, ExitShiftMetres: 40f));

        Assert.Equal(AdviceKind.Overdriving, advice.Kind);
        Assert.True(advice.BrakePointDelta > 9f, $"Bremspunkt-Delta {advice.BrakePointDelta:0} m.");
        Assert.True(advice.ApexSpeedDelta < -1f, $"Scheiteltempo-Delta {advice.ApexSpeedDelta:0.0} m/s.");
        Assert.Contains("zu spät gebremst", advice.Text);
        Assert.Contains("früher anbremsen", advice.SpeechText);
    }

    [Fact]
    public void Meldet_nur_die_tatsaechlich_verpatzte_Kurve()
    {
        (CornerAdvice advice, LapAnalysis analysis) = AnalyzeSingleCornerMistake(
            cornerIndex: 5,
            new CornerStyle(SpeedScale: 0.80f));

        CornerAdvice[] actionable = analysis.WorstFirst.ToArray();

        Assert.NotEmpty(actionable);
        Assert.Equal(advice.Corner.Number, actionable[0].Corner.Number);
        Assert.Equal(6, actionable[0].Corner.Number);
    }

    [Fact]
    public void Lobt_eine_schneller_gefahrene_Kurve()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        var styles = VirtualDriver.NeutralStyles();
        styles[4] = new CornerStyle(SpeedScale: 1.15f);

        LapCompleted lap = VirtualDriver.DriveLap(styles);
        LapAnalysis analysis = new LapAnalyzer().Analyze(lap.Lap, reference, corners);

        Assert.Equal(AdviceKind.Faster, analysis.Corners[4].Kind);
        Assert.True(analysis.Corners[4].TimeLost < 0f);
        Assert.DoesNotContain(analysis.WorstFirst, a => a.Corner.Number == 5);
    }
}
