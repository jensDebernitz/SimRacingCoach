using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft, ob die Fahrweise richtig gelesen wird – nicht nur, wo Zeit verloren
/// geht, sondern was der Fahrer mit Händen und Füßen anders macht.
/// </summary>
/// <remarks>
/// Alle Runden kommen aus dem virtuellen Fahrer und laufen durch den echten
/// <see cref="LapRecorder"/>. Ein Test, der sich seine Kanäle selbst malt,
/// würde die Abtastung überspringen und damit ausgerechnet die Stelle
/// auslassen, an der die Messung schiefgehen kann.
/// </remarks>
public class TechniqueAnalyzerTests
{
    /// <summary>Die Kurve, an der die Einlenkfehler geprüft werden (Index in <see cref="SimTrack.Default"/>).</summary>
    private const int TestCornerIndex = 0;

    [Fact]
    public void Eine_saubere_Runde_bekommt_keinen_Technikbefund()
    {
        (LapAnalysis analysis, _) = Analyze(VirtualDriver.NeutralStyles());

        CornerAdvice[] flagged = analysis.Corners.Where(c => c.Technique.HasFinding).ToArray();

        Assert.True(
            flagged.Length == 0,
            "Die Referenzlinie gegen sich selbst darf keinen Fahrfehler ergeben, gemeldet wurde aber: " +
            string.Join(" | ", flagged.Select(c => $"{c.Corner.Name} {c.Technique.Flags}")));
    }

    [Fact]
    public void Zu_frueh_eingelenkt_wird_erkannt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.True(
            technique.Has(TechniqueFlag.TurnInTooEarly),
            $"Erwartet wurde TurnInTooEarly, erkannt wurde: {technique.Flags} bei {technique.TurnInDelta:0} m");

        Assert.True(technique.TurnInDelta < -20f, $"Einlenkpunkt nur {technique.TurnInDelta:0} m verschoben.");
    }

    [Fact]
    public void Zu_spaet_eingelenkt_wird_erkannt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, TurnInShiftMetres: +30f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.True(
            technique.Has(TechniqueFlag.TurnInTooLate),
            $"Erwartet wurde TurnInTooLate, erkannt wurde: {technique.Flags} bei {technique.TurnInDelta:0} m");
    }

    /// <summary>
    /// Genau der vom Fahrer beschriebene Fall: "ich lenke zu früh ein, wenn ich
    /// noch auf der Bremse stehe".
    /// </summary>
    [Fact]
    public void Einlenken_unter_vollem_Bremsdruck_wird_erkannt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.True(
            technique.Has(TechniqueFlag.BrakingIntoTurnIn),
            $"Erwartet wurde BrakingIntoTurnIn, erkannt wurde: {technique.Flags} " +
            $"bei Bremsdruck {technique.Driven.BrakeAtTurnIn:0.00}");

        Assert.True(
            technique.Driven.BrakeAtTurnIn > 0.5f,
            $"Beim Einlenken lag nur ein Bremsdruck von {technique.Driven.BrakeAtTurnIn:0.00} an.");
    }

    [Fact]
    public void Zu_frueh_aufs_Gas_wird_erkannt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, ExitShiftMetres: -90f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.True(
            technique.Has(TechniqueFlag.ThrottleTooEarly),
            $"Erwartet wurde ThrottleTooEarly, erkannt wurde: {technique.Flags} " +
            $"bei {technique.Driven.ThrottleVsApexMetres:0} m zum Lenk-Scheitelpunkt");
    }

    [Fact]
    public void Ein_zu_frueher_Scheitelpunkt_wird_erkannt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, ExitShiftMetres: -90f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.True(
            technique.Has(TechniqueFlag.EarlyApex),
            $"Erwartet wurde EarlyApex, erkannt wurde: {technique.Flags} " +
            $"bei Scheitellage {technique.Driven.ApexPosition:0.00}");
    }

    [Fact]
    public void Der_Befund_nennt_den_Einlenkfehler_und_nicht_nur_den_Zeitverlust()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        TechniqueReport technique = TechniqueOf(styles, TestCornerIndex);

        Assert.Contains("einlenk", technique.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(technique.Speech));

        // Der Text wird hinter den Zeitverlust derselben Kurve gehängt – ein
        // zweites "Kurve 1" in derselben Zeile wäre doppelt.
        Assert.DoesNotContain("Kurve", technique.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Ansage muss die Fahrweise nennen, nicht die Zehntel: "du lenkst zu
    /// früh ein" kann der Fahrer umsetzen, "0,2 s verloren" nicht.
    /// </summary>
    [Fact]
    public void Die_Ansage_bevorzugt_den_Technikbefund()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[TestCornerIndex] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        (LapAnalysis analysis, IReadOnlyList<Corner> corners) = Analyze(styles);
        CornerAdvice advice = Advice(analysis, corners, TestCornerIndex);

        Assert.Equal(advice.Technique.Speech, advice.BestSpeechText);
    }

    [Fact]
    public void Ohne_Befund_bleibt_die_gerechnete_Ansage_stehen()
    {
        var advice = new CornerAdvice(
            new Corner(3, 10, 20, 15, 5, 22, CornerDirection.Left, 25f, 60f),
            AdviceKind.BrakeLater,
            0.3f,
            -12f,
            -1.5f,
            4f,
            "Kurve 3: 12 m später bremsen",
            "Kurve 3: du bremst zu früh.");

        Assert.Equal("Kurve 3: du bremst zu früh.", advice.BestSpeechText);
        Assert.False(advice.Technique.HasFinding);
    }

    /// <summary>Wertet eine Runde gegen die saubere Referenzrunde aus.</summary>
    private static (LapAnalysis Analysis, IReadOnlyList<Corner> Corners) Analyze(
        IReadOnlyList<CornerStyle> styles,
        SimTrack? track = null)
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap(track);
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        Assert.True(corners.Count > 0, "Auf der Referenzrunde wurde keine Kurve erkannt.");

        LapCompleted driven = VirtualDriver.DriveLap(styles, track);
        Assert.True(driven.IsValid, driven.InvalidReason);

        return (new LapAnalyzer().Analyze(driven.Lap, reference, corners), corners);
    }

    private static TechniqueReport TechniqueOf(IReadOnlyList<CornerStyle> styles, int cornerIndex)
    {
        (LapAnalysis analysis, IReadOnlyList<Corner> corners) = Analyze(styles);
        return Advice(analysis, corners, cornerIndex).Technique;
    }

    /// <summary>
    /// Sucht die Auswertung zu einer Kurve der Teststrecke. Die Nummerierung des
    /// <see cref="CornerDetector"/> muss nicht mit der Reihenfolge in
    /// <see cref="SimTrack.Default"/> übereinstimmen, deshalb wird über die
    /// Distanz zugeordnet.
    /// </summary>
    private static CornerAdvice Advice(
        LapAnalysis analysis,
        IReadOnlyList<Corner> corners,
        int simCornerIndex,
        SimTrack? track = null)
    {
        track ??= SimTrack.Default;
        SimCorner target = track.Corners[simCornerIndex];
        float middle = target.EntryDistance + target.Length * 0.5f;

        Corner match = corners
            .OrderBy(c => MathF.Abs(((c.StartBin + c.EndBin) * 0.5f * LapRecorder.DefaultBinSize) - middle))
            .First();

        return analysis.Corners.Single(a => a.Corner.Number == match.Number);
    }
}
