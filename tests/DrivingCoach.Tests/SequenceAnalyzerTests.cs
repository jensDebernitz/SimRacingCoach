using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft das Zusammenfassen von Kurven zu Kombinationen und die Bewertung der
/// Kombination als Ganzes.
/// </summary>
/// <remarks>
/// Die Erkennung läuft gegen eine echte simulierte Runde, die Bewertung gegen
/// von Hand gesetzte Zeitverluste. Beides zusammen in einem Test würde
/// bedeuten, den Solver so lange zu justieren, bis die gewünschte Verteilung
/// herauskommt – und dann prüfte der Test den Solver, nicht die Bewertung.
/// </remarks>
public class SequenceAnalyzerTests
{
    /// <summary>
    /// Eine Strecke mit einer Schikane bei 700/860 m und einer einzeln
    /// stehenden Kurve bei 2000 m.
    /// </summary>
    /// <remarks>
    /// Der Abstand von 90 m ist mit Bedacht gewählt: unter 50 m würde der
    /// <see cref="CornerDetector"/> beide zu <em>einer</em> Kurve verschmelzen,
    /// über 130 m wären es für den <see cref="SequenceAnalyzer"/> zwei
    /// unabhängige Kurven.
    /// </remarks>
    private static readonly SimTrack Chicane = new(
        "Schikanenkurs",
        length: 4200f,
        corners:
        [
            new SimCorner(700f, 70f, 28f, +1),
            new SimCorner(860f, 70f, 28f, -1),
            new SimCorner(2000f, 100f, 34f, +1),
        ],
        topSpeed: 78f);

    [Fact]
    public void Zwei_dicht_aufeinanderfolgende_Kurven_werden_zur_Kombination()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap(Chicane);
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        Assert.Equal(3, corners.Count);

        IReadOnlyList<CornerSequence> sequences =
            SequenceAnalyzer.Detect(corners, reference.BinSize, reference.TrackLength);

        Assert.Equal(2, sequences.Count);

        CornerSequence combination = sequences[0];
        Assert.True(combination.IsCombination, $"{combination.Name} wurde nicht als Kombination erkannt.");
        Assert.Equal(2, combination.Corners.Count);
        Assert.Equal("rechts-links", combination.Shape);

        Assert.False(sequences[1].IsCombination, "Die einzeln stehende Kurve wurde einer Kombination zugeschlagen.");
    }

    /// <summary>
    /// Hinter der Schikane liegen über 1000 m bis zur nächsten Bremszone – der
    /// Ausgang der letzten Kurve wiegt dort schwer.
    /// </summary>
    [Fact]
    public void Die_Gerade_hinter_der_Kombination_wird_gemessen()
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap(Chicane);
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        CornerSequence combination =
            SequenceAnalyzer.Detect(corners, reference.BinSize, reference.TrackLength)[0];

        Assert.True(
            combination.LeadsOntoStraight,
            $"Nach der Kombination wurden nur {combination.FollowingStraightMetres:0} m Gerade gemessen.");

        Assert.InRange(combination.FollowingStraightMetres, 900f, 1300f);
    }

    [Fact]
    public void Verlust_in_der_letzten_Kurve_heisst_geopferter_Ausgang()
    {
        SequenceAdvice result = Evaluate(firstCornerLoss: 0.02f, lastCornerLoss: 0.30f);

        Assert.Equal(SequenceIssue.ExitCompromised, result.Issue);
        Assert.Contains("Opfere den Eingang", result.Text, StringComparison.Ordinal);
        Assert.Contains("m Gerade", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verlust_in_der_ersten_Kurve_heisst_ueberfahrener_Eingang()
    {
        SequenceAdvice result = Evaluate(firstCornerLoss: 0.30f, lastCornerLoss: 0.02f);

        Assert.Equal(SequenceIssue.EntryOverdriven, result.Issue);
        Assert.Contains("überfährst", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Gleichmaessiger_Verlust_heisst_durchgehende_Linie()
    {
        SequenceAdvice result = Evaluate(firstCornerLoss: 0.15f, lastCornerLoss: 0.15f);

        Assert.Equal(SequenceIssue.Throughout, result.Issue);
        Assert.Contains("einen Bogen", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_saubere_Kombination_bekommt_keinen_Hinweis()
    {
        SequenceAdvice result = Evaluate(firstCornerLoss: 0.01f, lastCornerLoss: 0.02f);

        Assert.Equal(SequenceIssue.None, result.Issue);
        Assert.False(result.IsActionable);
        Assert.Equal(string.Empty, result.Text);
    }

    /// <summary>
    /// Eine einzeln stehende Kurve ist keine Kombination und bekommt deshalb
    /// gar keine Bewertung – sonst stünde derselbe Hinweis zweimal im Overlay.
    /// </summary>
    [Fact]
    public void Einzelne_Kurven_werden_nicht_bewertet()
    {
        Corner solo = MakeCorner(1, start: 100);
        var sequence = new CornerSequence(1, [solo], FollowingStraightMetres: 800f);

        IReadOnlyList<SequenceAdvice> result = SequenceAnalyzer.Analyze(
            [sequence],
            [MakeAdvice(solo, timeLost: 0.5f)]);

        Assert.Empty(result);
    }

    /// <summary>Bewertet eine Zweierkombination mit vorgegebener Verlustverteilung.</summary>
    private static SequenceAdvice Evaluate(float firstCornerLoss, float lastCornerLoss)
    {
        Corner first = MakeCorner(1, start: 100);
        Corner last = MakeCorner(2, start: 140);

        var sequence = new CornerSequence(1, [first, last], FollowingStraightMetres: 640f);

        IReadOnlyList<SequenceAdvice> result = SequenceAnalyzer.Analyze(
            [sequence],
            [MakeAdvice(first, firstCornerLoss), MakeAdvice(last, lastCornerLoss)]);

        return Assert.Single(result);
    }

    private static Corner MakeCorner(int number, int start) => new(
        Number: number,
        StartBin: start,
        EndBin: start + 20,
        ApexBin: start + 10,
        BrakingStartBin: start - 10,
        ThrottleBin: start + 18,
        Direction: number % 2 == 1 ? CornerDirection.Right : CornerDirection.Left,
        ApexSpeed: 25f,
        EntrySpeed: 60f);

    private static CornerAdvice MakeAdvice(Corner corner, float timeLost) => new(
        corner,
        timeLost > 0.05f ? AdviceKind.MoreApexSpeed : AdviceKind.Neutral,
        timeLost,
        BrakePointDelta: 0f,
        ApexSpeedDelta: 0f,
        ThrottleDelta: 0f,
        Text: $"{corner.Name}: Testhinweis",
        SpeechText: $"{corner.Name}: Testansage.");
}
