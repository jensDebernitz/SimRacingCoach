using System.Globalization;
using System.Text;
using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Ai;

/// <summary>
/// Übersetzt eine ausgewertete Runde in einen kompakten Zahlenblock für das
/// Modell.
/// </summary>
/// <remarks>
/// <para>
/// Bewusst eine Tabelle und kein Fließtext. Das Modell soll die Zahlen
/// vergleichen und nicht meine Formulierungen nachplappern – hätte ich schon
/// "du bremst zu früh" hineingeschrieben, käme genau das wieder heraus und die
/// Anfrage wäre umsonst gewesen.
/// </para>
/// <para>
/// Alle Zahlen mit Punkt als Dezimaltrennzeichen: das Modell bekommt
/// maschinenlesbare Werte, die deutsche Schreibweise entsteht erst in der
/// Antwort.
/// </para>
/// </remarks>
public static class LapDigest
{
    private static readonly CultureInfo Machine = CultureInfo.InvariantCulture;

    /// <summary>Wie viele Kurven höchstens in den Prompt wandern.</summary>
    private const int MaxCornerRows = 12;

    /// <summary>Beschreibt eine einzelne Runde samt Kurventabelle.</summary>
    public static string Describe(LapReview review)
    {
        var text = new StringBuilder();

        SessionInfo session = review.Session;
        LapAnalysis analysis = review.Analysis;

        text.Append("Strecke: ").AppendLine(session.TrackDisplayName);
        text.Append("Auto: ").AppendLine(session.CarName);
        text.Append("Runde Nummer: ").Append(review.LapNumber).AppendLine();
        text.Append("Rundenzeit: ").AppendLine(Seconds(analysis.LapTime));
        text.Append("Referenzzeit: ").AppendLine(Seconds(analysis.ReferenceLapTime));
        text.Append("Differenz: ").Append(Signed(analysis.LapDelta)).AppendLine(" s");
        text.Append("In Kurven verloren: ").Append(Number(analysis.TotalTimeLost)).AppendLine(" s");

        if (review.IsNewBest)
        {
            text.AppendLine("Hinweis: Das war die neue Bestzeit.");
        }

        text.AppendLine();
        AppendCornerTable(text, analysis);
        AppendTechniqueTable(text, analysis);
        AppendSequences(text, analysis);

        return text.ToString();
    }

    /// <summary>
    /// Die Kurventabelle. Nur Kurven mit Handlungsbedarf und die stärksten
    /// Gewinne – eine Zeile "passt" pro Kurve wäre Ballast.
    /// </summary>
    private static void AppendCornerTable(StringBuilder text, LapAnalysis analysis)
    {
        CornerAdvice[] rows = analysis.Corners
            .Where(c => c.IsActionable || c.Kind == AdviceKind.Faster)
            .OrderByDescending(c => c.TimeLost)
            .Take(MaxCornerRows)
            .ToArray();

        if (rows.Length == 0)
        {
            text.AppendLine("Keine auffaellige Kurve: der Fahrer war ueberall nah an der Referenz.");
            return;
        }

        text.AppendLine("Kurven (Abweichung gegenueber der Referenzrunde):");
        text.AppendLine("nr|typ|richtung|scheitel_kmh|bremspunkt_m|scheiteltempo_kmh|gaspunkt_m|verlust_s|befund");

        foreach (CornerAdvice advice in rows)
        {
            Corner corner = advice.Corner;

            text.Append(corner.Number).Append('|')
                .Append(Category(corner)).Append('|')
                .Append(corner.Direction == CornerDirection.Left ? "links" : "rechts").Append('|')
                .Append(Number(corner.ApexSpeedKmh, 0)).Append('|')
                .Append(Signed(advice.BrakePointDelta, 0)).Append('|')
                .Append(Signed(advice.ApexSpeedDelta * 3.6f, 0)).Append('|')
                .Append(Signed(advice.ThrottleDelta, 0)).Append('|')
                .Append(Signed(advice.TimeLost)).Append('|')
                .AppendLine(Finding(advice.Kind));
        }

        text.AppendLine();
        text.AppendLine("Lesehilfe: bremspunkt_m positiv = spaeter gebremst als die Referenz, negativ = frueher.");
        text.AppendLine("scheiteltempo_kmh negativ = langsamer am Scheitelpunkt. gaspunkt_m positiv = spaeter aufs Gas.");
        text.AppendLine("verlust_s positiv = Zeit verloren, negativ = Zeit gewonnen.");
    }

    /// <summary>
    /// Die Fahrweise je Kurve: Einlenkpunkt, Bremsdruck beim Einlenken,
    /// Scheitelpunkt, Gasannahme.
    /// </summary>
    /// <remarks>
    /// Rohe Messwerte <em>und</em> die erkannten Merkmale. Die Merkmale allein
    /// würden das Modell auf meine Schwellenwerte festnageln; die Messwerte
    /// allein müsste es selbst deuten und läge dabei gelegentlich daneben. Mit
    /// beidem kann es die Merkmale in Zusammenhang bringen – und genau das ist
    /// der Teil, den ich nicht ausrechnen kann.
    /// </remarks>
    private static void AppendTechniqueTable(StringBuilder text, LapAnalysis analysis)
    {
        CornerAdvice[] rows = analysis.TechniqueFindings.Take(MaxCornerRows).ToArray();
        if (rows.Length == 0)
        {
            text.AppendLine("Fahrweise: keine Auffaelligkeit bei Einlenkpunkt, Bremsdruck oder Gasannahme.");
            text.AppendLine();
            return;
        }

        text.AppendLine("Fahrweise (wie die Kurve gefahren wurde):");
        text.AppendLine(
            "nr|einlenkpunkt_m|bremsdruck_beim_einlenken|mitbremsen_m|scheitellage|gas_vs_scheitel_m|lenkkorrekturen|merkmale");

        foreach (CornerAdvice advice in rows)
        {
            TechniqueReport technique = advice.Technique;
            CornerTechnique driven = technique.Driven;

            text.Append(advice.Corner.Number).Append('|')
                .Append(Signed(technique.TurnInDelta, 0)).Append('|')
                .Append(Number(driven.BrakeAtTurnIn)).Append('|')
                .Append(Number(driven.TrailBrakeMetres, 0)).Append('|')
                .Append(Number(driven.ApexPosition)).Append('|')
                .Append(Signed(driven.ThrottleVsApexMetres, 0)).Append('|')
                .Append(driven.SteeringReversals).Append('|')
                .AppendLine(Flags(technique.Flags));
        }

        text.AppendLine();
        text.AppendLine("Lesehilfe: einlenkpunkt_m positiv = spaeter eingelenkt als die Referenz, negativ = frueher.");
        text.AppendLine("bremsdruck_beim_einlenken 0 bis 1; ueber 0,75 heisst voll auf der Bremse eingelenkt.");
        text.AppendLine("mitbremsen_m = Meter, auf denen gleichzeitig gebremst und gelenkt wurde.");
        text.AppendLine("scheitellage 0 bis 1: Lage des langsamsten Punkts im Bogen, 0,5 ist mittig, unter 0,4 ist frueh.");
        text.AppendLine("gas_vs_scheitel_m negativ = schon vor dem groessten Lenkeinschlag am Gas.");
        text.AppendLine();
    }

    /// <summary>Kurvenkombinationen – dort entscheidet sich, welche Kurve wichtig ist.</summary>
    private static void AppendSequences(StringBuilder text, LapAnalysis analysis)
    {
        SequenceAdvice[] rows = analysis.WorstSequencesFirst.Take(6).ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        text.AppendLine("Kurvenkombinationen:");
        text.AppendLine("kurven|richtungen|gerade_danach_m|verlust_s|befund");

        foreach (SequenceAdvice row in rows)
        {
            text.Append(row.Sequence.First.Number).Append('-').Append(row.Sequence.Last.Number).Append('|')
                .Append(row.Sequence.Shape).Append('|')
                .Append(Number(row.Sequence.FollowingStraightMetres, 0)).Append('|')
                .Append(Signed(row.TimeLost)).Append('|')
                .AppendLine(SequenceFinding(row.Issue));
        }

        text.AppendLine();
        text.AppendLine("Lesehilfe: gerade_danach_m = Meter bis zum naechsten Bremspunkt. Je laenger, desto");
        text.AppendLine("wichtiger ist der Ausgang der letzten Kurve der Kombination.");
        text.AppendLine();
    }

    /// <summary>Fasst mehrere Runden als Verlauf zusammen – Grundlage des Session-Fazits.</summary>
    public static string DescribeSession(IReadOnlyList<LapReview> laps)
    {
        if (laps.Count == 0)
        {
            return "Keine ausgewerteten Runden.";
        }

        var text = new StringBuilder();
        LapReview last = laps[^1];

        text.Append("Strecke: ").AppendLine(last.Session.TrackDisplayName);
        text.Append("Auto: ").AppendLine(last.Session.CarName);
        text.Append("Ausgewertete Runden: ").Append(laps.Count).AppendLine();
        text.Append("Beste Zeit: ").AppendLine(Seconds(laps.Min(l => l.Analysis.LapTime)));
        text.AppendLine();

        text.AppendLine("Rundenverlauf:");
        text.AppendLine("runde|zeit|zeit_s|differenz_s|in_kurven_verloren_s");

        foreach (LapReview lap in laps)
        {
            text.Append(lap.LapNumber).Append('|')
                .Append(CoachEngine.FormatLapTime(lap.Analysis.LapTime)).Append('|')
                .Append(Number(lap.Analysis.LapTime)).Append('|')
                .Append(Signed(lap.Analysis.LapDelta)).Append('|')
                .AppendLine(Number(lap.Analysis.TotalTimeLost));
        }

        text.AppendLine();

        // Ohne diese Zeile rechnet das Modell die Sekundenzahl selbst um und
        // mischt beide Schreibweisen im selben Absatz: "1:38,793" neben
        // "100,69 s". Die fertige Form steht deshalb schon in der Tabelle.
        text.AppendLine("Lesehilfe: zeit ist die lesbare Form von zeit_s. Nenne Rundenzeiten immer so");
        text.AppendLine("(m:ss,mmm), nie als blosse Sekundenzahl. Zeitunterschiede dagegen in Sekunden.");
        text.AppendLine();
        AppendRepeatOffenders(text, laps);

        return text.ToString();
    }

    /// <summary>
    /// Die Kurven, die über die ganze Sitzung hinweg kosten. Genau das ist der
    /// Mehrwert gegenüber dem Einzelrunden-Bericht: eine Kurve, die einmal
    /// danebengeht, ist Pech – eine, die achtmal danebengeht, ist Technik.
    /// </summary>
    private static void AppendRepeatOffenders(StringBuilder text, IReadOnlyList<LapReview> laps)
    {
        var tally = new Dictionary<int, (int Count, float Total, AdviceKind Kind, string Category)>();

        foreach (LapReview lap in laps)
        {
            foreach (CornerAdvice advice in lap.Analysis.Corners.Where(c => c.IsActionable && c.TimeLost > 0f))
            {
                int number = advice.Corner.Number;
                tally.TryGetValue(number, out (int Count, float Total, AdviceKind Kind, string Category) entry);

                tally[number] = (
                    entry.Count + 1,
                    entry.Total + advice.TimeLost,
                    advice.Kind,
                    Category(advice.Corner));
            }
        }

        if (tally.Count == 0)
        {
            text.AppendLine("Keine Kurve faellt wiederholt auf.");
            return;
        }

        text.AppendLine("Wiederkehrende Problemkurven (ueber alle Runden summiert):");
        text.AppendLine("nr|typ|betroffene_runden|summe_verlust_s|haeufigster_befund");

        foreach ((int number, (int count, float total, AdviceKind kind, string category)) in
                 tally.OrderByDescending(e => e.Value.Total).Take(MaxCornerRows))
        {
            text.Append(number).Append('|')
                .Append(category).Append('|')
                .Append(count).Append('|')
                .Append(Number(total)).Append('|')
                .AppendLine(Finding(kind));
        }
    }

    /// <summary>Kurzbeschreibung des aktuellen Fahrzustands für freie Fragen.</summary>
    public static string DescribeState(CoachState state)
    {
        var text = new StringBuilder();
        TelemetryFrame frame = state.Frame;
        SessionInfo session = state.Session;

        text.Append("Strecke: ").AppendLine(session.IsUsable ? session.TrackDisplayName : "unbekannt");
        text.Append("Auto: ").AppendLine(string.IsNullOrEmpty(session.CarName) ? "unbekannt" : session.CarName);
        text.Append("Gueltige Runden dieser Sitzung: ").Append(state.ValidLapCount).AppendLine();

        text.Append("Referenzrunde: ")
            .AppendLine(state.Reference is { } reference ? Seconds(reference.LapTime) : "noch keine");

        text.Append("Erkannte Kurven: ").Append(state.Corners.Count).AppendLine();
        text.Append("Aktuell auf der Strecke: ").AppendLine(frame.IsDriving ? "ja" : "nein");

        if (state.LastAnalysis is { } analysis)
        {
            text.AppendLine();
            text.AppendLine("Letzte ausgewertete Runde:");
            text.Append("Rundenzeit: ").AppendLine(Seconds(analysis.LapTime));
            text.Append("Differenz: ").Append(Signed(analysis.LapDelta)).AppendLine(" s");
            AppendCornerTable(text, analysis);
        }

        return text.ToString();
    }

    private static string Category(Corner corner) => corner.SpeedCategory switch
    {
        "langsame" => "langsam",
        "mittelschnelle" => "mittel",
        _ => "schnell",
    };

    /// <summary>
    /// Die erkannten Merkmale als Klartext. Bewusst ausgeschriebene deutsche
    /// Begriffe statt der Enum-Namen – das Modell soll sie im Fließtext
    /// verwenden können, ohne sie erst übersetzen zu müssen.
    /// </summary>
    private static string Flags(TechniqueFlag flags)
    {
        if (flags == TechniqueFlag.None)
        {
            return "unauffaellig";
        }

        var names = new List<string>(4);

        void Add(TechniqueFlag flag, string name)
        {
            if ((flags & flag) != 0)
            {
                names.Add(name);
            }
        }

        Add(TechniqueFlag.PedalOverlap, "Gas und Bremse gleichzeitig");
        Add(TechniqueFlag.BrakingIntoTurnIn, "unter vollem Bremsdruck eingelenkt");
        Add(TechniqueFlag.TurnInTooEarly, "zu frueh eingelenkt");
        Add(TechniqueFlag.TurnInTooLate, "zu spaet eingelenkt");
        Add(TechniqueFlag.EarlyApex, "Scheitelpunkt zu frueh");
        Add(TechniqueFlag.LateApex, "Scheitelpunkt zu spaet");
        Add(TechniqueFlag.AddingLockOnExit, "am Ausgang nachgelenkt");
        Add(TechniqueFlag.ThrottleTooEarly, "zu frueh am Gas");
        Add(TechniqueFlag.ThrottleTooLate, "zu spaet am Gas");
        Add(TechniqueFlag.NoTrailBraking, "Bremse vor dem Einlenken ganz geloest");
        Add(TechniqueFlag.RestlessSteering, "unruhig am Lenkrad");

        return string.Join(", ", names);
    }

    private static string SequenceFinding(SequenceIssue issue) => issue switch
    {
        SequenceIssue.ExitCompromised => "Verlust in der letzten Kurve, Ausgang geopfert",
        SequenceIssue.EntryOverdriven => "Verlust in der ersten Kurve, Eingang ueberfahren",
        SequenceIssue.Throughout => "ueber die ganze Kombination verteilt",
        _ => "unauffaellig",
    };

    private static string Finding(AdviceKind kind) => kind switch
    {
        AdviceKind.BrakeLater => "zu frueh gebremst",
        AdviceKind.MoreApexSpeed => "zu wenig Tempo am Scheitelpunkt",
        AdviceKind.EarlierThrottle => "zu spaet wieder am Gas",
        AdviceKind.Overdriving => "zu spaet gebremst und den Scheitelpunkt verpasst",
        AdviceKind.Faster => "schneller als die Referenz",
        _ => "unauffaellig",
    };

    private static string Seconds(float value) =>
        $"{Number(value)} s ({CoachEngine.FormatLapTime(value)})";

    private static string Number(float value, int decimals = 2) =>
        value.ToString("F" + decimals.ToString(Machine), Machine);

    private static string Signed(float value, int decimals = 2)
    {
        string pattern = decimals <= 0 ? "0" : "0." + new string('0', decimals);
        return value.ToString($"+{pattern};-{pattern};0", Machine);
    }
}
