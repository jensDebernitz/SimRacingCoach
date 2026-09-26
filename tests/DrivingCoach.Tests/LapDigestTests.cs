using System.Globalization;
using DrivingCoach.Ai;
using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft den Zahlenblock, den Gemini zu sehen bekommt.
/// </summary>
/// <remarks>
/// Ein Fehler hier fällt im Betrieb nicht auf: Das Modell antwortet auch auf
/// eine halbe Tabelle flüssig – nur eben falsch. Deshalb steht unter Test, dass
/// die Messwerte überhaupt ankommen und maschinenlesbar bleiben.
/// </remarks>
public class LapDigestTests
{
    [Fact]
    public void Die_Fahrweise_steht_im_Prompt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[0] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        string digest = LapDigest.Describe(Review(styles));

        Assert.Contains("Fahrweise (wie die Kurve gefahren wurde):", digest, StringComparison.Ordinal);
        Assert.Contains("einlenkpunkt_m", digest, StringComparison.Ordinal);
        Assert.Contains("zu frueh eingelenkt", digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne die Lesehilfe müsste das Modell raten, in welche Richtung ein
    /// negativer Einlenkpunkt zeigt – und es rät in beide.
    /// </summary>
    [Fact]
    public void Die_Fahrweise_bringt_ihre_Lesehilfe_mit()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[0] = new CornerStyle(1f, TurnInShiftMetres: -40f);

        string digest = LapDigest.Describe(Review(styles));

        Assert.Contains("einlenkpunkt_m positiv = spaeter eingelenkt", digest, StringComparison.Ordinal);
        Assert.Contains("scheitellage 0 bis 1", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_saubere_Runde_sagt_das_auch_so()
    {
        string digest = LapDigest.Describe(Review(VirtualDriver.NeutralStyles()));

        Assert.Contains("Fahrweise: keine Auffaelligkeit", digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Prompt ist maschinenlesbar, die Antwort deutsch. Auf einem deutschen
    /// Windows wäre "0,23" ohne die feste Kultur der Normalfall – und das
    /// Modell läse daraus zwei Zahlen.
    /// </summary>
    [Fact]
    public void Zahlen_bleiben_auch_auf_deutschem_System_mit_Punkt()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[3] = new CornerStyle(0.86f);

        string digest = InGerman(() => LapDigest.Describe(Review(styles)));

        string table = Section(digest, "nr|typ|richtung|");

        Assert.Contains('.', table);
        Assert.DoesNotContain(',', table);
    }

    [Fact]
    public void Das_Session_Fazit_listet_jede_Runde_und_die_Dauerbrenner()
    {
        CornerStyle[] styles = VirtualDriver.NeutralStyles();
        styles[3] = new CornerStyle(0.86f);

        LapReview[] laps = [Review(styles, lapNumber: 2), Review(styles, lapNumber: 3)];

        string digest = LapDigest.DescribeSession(laps);

        Assert.Contains("Ausgewertete Runden: 2", digest, StringComparison.Ordinal);
        Assert.Contains("runde|zeit|zeit_s|differenz_s", digest, StringComparison.Ordinal);
        Assert.Contains("Wiederkehrende Problemkurven", digest, StringComparison.Ordinal);

        // Dieselbe Kurve in beiden Runden: betroffene_runden muss 2 sein.
        Assert.Contains("|2|", Section(digest, "nr|typ|betroffene_runden"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Bekommt das Modell nur Sekunden, rechnet es selbst um – und mischt dann
    /// beide Schreibweisen im selben Absatz ("1:38,793" neben "100,69 s").
    /// Die lesbare Form steht deshalb fertig in der Tabelle.
    /// </summary>
    [Fact]
    public void Das_Fazit_liefert_die_Rundenzeit_schon_lesbar_mit()
    {
        string digest = LapDigest.DescribeSession([Review(VirtualDriver.NeutralStyles(), lapNumber: 2)]);

        Assert.Matches(@"^2\|\d+:\d\d,\d\d\d\|", Section(digest, "runde|zeit|zeit_s"));
        Assert.Contains("Nenne Rundenzeiten immer so", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Runden_bleibt_das_Fazit_ein_Satz()
    {
        Assert.Equal("Keine ausgewerteten Runden.", LapDigest.DescribeSession([]));
    }

    /// <summary>Wertet eine Runde gegen die saubere Referenzrunde aus.</summary>
    private static LapReview Review(IReadOnlyList<CornerStyle> styles, int lapNumber = 2)
    {
        RecordedLap reference = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(reference);

        LapCompleted driven = VirtualDriver.DriveLap(styles);
        Assert.True(driven.IsValid, driven.InvalidReason);

        LapAnalysis analysis = new LapAnalyzer().Analyze(driven.Lap, reference, corners);

        return new LapReview(VirtualDriver.Session, analysis, lapNumber, IsNewBest: false);
    }

    /// <summary>
    /// Führt etwas unter deutscher Kultur aus und stellt sie danach zurück.
    /// </summary>
    private static string InGerman(Func<string> action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Die Zeilen ab einer Kopfzeile bis zur nächsten Leerzeile.</summary>
    private static string Section(string digest, string header)
    {
        string[] lines = digest.Split(Environment.NewLine);
        int start = Array.FindIndex(lines, l => l.StartsWith(header, StringComparison.Ordinal));

        Assert.True(start >= 0, $"Die Kopfzeile \"{header}\" fehlt im Prompt:{Environment.NewLine}{digest}");

        int end = Array.FindIndex(lines, start + 1, string.IsNullOrWhiteSpace);
        if (end < 0)
        {
            end = lines.Length;
        }

        return string.Join(Environment.NewLine, lines[(start + 1)..end]);
    }
}
