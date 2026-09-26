using System.Text.Json.Nodes;

namespace DrivingCoach.Ai;

/// <summary>
/// Rollenbeschreibungen und Antwortschemata für die Anfragen an Gemini.
/// </summary>
/// <remarks>
/// <para>
/// Alle Anfragen teilen sich dieselbe Grundregel: Das Modell darf ausschließlich
/// mit den übergebenen Zahlen arbeiten. Ein Fahrtrainer, der sich Werte
/// ausdenkt, schickt einen Anfänger mit voller Überzeugung an der falschen
/// Stelle auf die Bremse.
/// </para>
/// <para>
/// Wo die Antwort weiterverarbeitet wird, ist ein Schema hinterlegt. Damit
/// erzwingt der Dienst gültiges JSON, und es muss kein Fließtext zerlegt werden.
/// </para>
/// </remarks>
public static class Prompts
{
    /// <summary>Gilt für alle Anfragen.</summary>
    private const string Base = """
        Du bist Renningenieur und Fahrtrainer fuer einen blutigen Anfaenger im
        Sim-Racing. Er faehrt Automobilista 2 mit einem Direct-Drive-Lenkrad.

        Feste Regeln:
        - Antworte auf Deutsch, in der Du-Form, ohne Anrede und ohne Gruss.
        - Nutze ausschliesslich die uebergebenen Zahlen. Erfinde niemals Werte,
          Kurvennamen, Streckenabschnitte oder Rundenzeiten.
        - Wenn die Daten fuer eine Aussage nicht reichen, sag das knapp.
        - Keine Aufzaehlungszeichen, keine Sternchen, keine Ueberschriften.
        - Schreib Zahlen in deutscher Schreibweise: 0,21 s und 1:33,762.
        - Erklaere fahrerisch, nicht physikalisch. Der Fahrer will wissen, was er
          mit Fuss und Haenden anders machen soll.
        """;

    /// <summary>Rolle für die Rundenbesprechung.</summary>
    public const string DebriefSystem = $"""
        {Base}

        Du bekommst die Auswertung einer einzelnen Runde gegen die Referenzrunde
        desselben Fahrers. Der Fahrer hat den nackten Zahlenbericht bereits im
        Overlay gesehen. Dein Beitrag ist das, was die Zahlen nicht zeigen: das
        gemeinsame Muster hinter den Einzelfehlern und die eine Sache, die er
        als Naechstes angehen soll.
        """;

    /// <summary>Rolle für die vorformulierten Live-Ansagen.</summary>
    public const string LiveSystem = $"""
        {Base}

        Du formulierst Ansagen, die dem Fahrer waehrend der Fahrt ueber
        Sprachausgabe vorgelesen werden – oft mitten im Anbremsen.

        Dafuer gilt zusaetzlich:
        - Hoechstens acht Woerter je Ansage. Kuerzer ist besser.
        - Ein einziger Imperativ. Keine Begruendung, kein Nebensatz.
        - Keine Zahlen, keine Einheiten: gesprochene Zahlen kann der Fahrer im
          Sekundenbruchteil nicht umsetzen.
        - Nichts, was zum Nachdenken einlaedt. Nur, was sofort ausfuehrbar ist.
        """;

    /// <summary>Rolle für das Session-Fazit.</summary>
    public const string SummarySystem = $"""
        {Base}

        Du schreibst das Fazit einer kompletten Session. Der Fahrer sitzt nicht
        mehr im Auto und hat Zeit zu lesen.

        Gliedere in genau drei Absaetze, ohne Ueberschriften:
        1. Was sich ueber die Session hinweg verbessert hat, mit Zahlen belegt.
        2. Der groesste verbleibende Zeitverlust und woran er liegt.
        3. Ein konkreter Uebungsauftrag fuer die naechste Session: was, wo, wie
           viele Runden und woran er merkt, dass es klappt.
        """;

    /// <summary>Rolle für freie Fragen.</summary>
    public const string QuestionSystem = $"""
        {Base}

        Du beantwortest eine freie Frage des Fahrers. Er sitzt in der Box und
        hat Zeit fuer drei bis fuenf Saetze.

        Steht die Antwort nicht in den Daten, sag das offen und erklaere
        stattdessen allgemein – aber kennzeichne, dass es allgemein ist.
        """;

    /// <summary>
    /// Antwortschema der Rundenbesprechung: Fliesstext plus je Kurve eine
    /// fertige Ansage für die naechste Runde.
    /// </summary>
    public static JsonNode DebriefSchema() => new JsonObject
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["debrief"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Zwei bis drei Saetze Rundenbesprechung fuer das Overlay.",
            },
            ["spoken"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Dieselbe Kernaussage in einem einzigen kurzen Satz zum Vorlesen.",
            },
            ["cornerLines"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["description"] = "Je auffaelliger Kurve eine Ansage fuer die naechste Runde.",
                ["items"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["corner"] = new JsonObject { ["type"] = "INTEGER" },
                        ["speech"] = new JsonObject { ["type"] = "STRING" },
                    },
                    ["required"] = new JsonArray("corner", "speech"),
                },
            },
        },
        ["required"] = new JsonArray("debrief", "spoken", "cornerLines"),
    };

    /// <summary>Antwortschema für den Vorrat an Fahrfehler-Ansagen.</summary>
    public static JsonNode FaultSchema() => new JsonObject
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["faults"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["items"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["kind"] = new JsonObject { ["type"] = "STRING" },
                        ["variants"] = new JsonObject
                        {
                            ["type"] = "ARRAY",
                            ["items"] = new JsonObject { ["type"] = "STRING" },
                        },
                    },
                    ["required"] = new JsonArray("kind", "variants"),
                },
            },
        },
        ["required"] = new JsonArray("faults"),
    };

    /// <summary>Die Aufgabe zur Rundenbesprechung, mit dem Zahlenblock dahinter.</summary>
    public static string DebriefPrompt(string digest) => $"""
        Hier die Auswertung der eben gefahrenen Runde:

        {digest}

        Liefere:

        1. "debrief": zwei bis drei Saetze. Benenne das gemeinsame Muster hinter
           den Einzelfehlern, falls es eines gibt – etwa durchgehend zu frueh
           gebremst oder nur in schnellen Kurven zu vorsichtig. Nenne dann die
           eine Sache, die in der naechsten Runde am meisten bringt, mit dem
           Zeitgewinn, der laut Tabelle darin steckt.

        2. "spoken": dieselbe Kernaussage in einem Satz, hoechstens zwoelf
           Woerter, zum Vorlesen zwischen zwei Runden.

        3. "cornerLines": fuer jede Kurve mit positivem Verlust eine Ansage, die
           dem Fahrer in der naechsten Runde kurz vor dem Bremspunkt vorgelesen
           wird. Hoechstens acht Woerter, ein Imperativ, keine Zahlen, keine
           Kurvennummer – die Kurve steht schon im Feld "corner". Hoechstens
           sechs Eintraege, die teuersten Kurven zuerst.
        """;

    /// <summary>
    /// Die Aufgabe für den Ansagen-Vorrat. Wird einmal je Sitzung gestellt, weil
    /// Fahrfehler nicht von der Strecke abhaengen.
    /// </summary>
    public static string FaultPrompt(IReadOnlyList<(string Kind, string Meaning)> faults)
    {
        string list = string.Join(
            Environment.NewLine,
            faults.Select(f => $"- {f.Kind}: {f.Meaning}"));

        return $"""
            Formuliere Warnungen, die dem Fahrer unmittelbar nach einem Fahrfehler
            vorgelesen werden. Hier die Fehlerarten und was sie bedeuten:

            {list}

            Gib zu jedem "kind" genau drei Fassungen in "variants" zurueck, die
            dasselbe sagen, aber unterschiedlich klingen. Uebernimm die
            Bezeichner unveraendert – sie werden als Schluessel verwendet.

            Jede Fassung: hoechstens acht Woerter, ein Imperativ oder eine knappe
            Feststellung, keine Zahlen. Der Fahrer faengt in dem Moment das Auto.
            """;
    }

    /// <summary>Die Aufgabe für das Session-Fazit.</summary>
    public static string SummaryPrompt(string digest) => $"""
        Hier der Verlauf der gesamten Session:

        {digest}

        Schreib das Fazit nach den drei Absaetzen aus deiner Rolle.
        """;

    /// <summary>Die Aufgabe für eine freie Frage.</summary>
    public static string QuestionPrompt(string state, string question) => $"""
        Aktueller Stand:

        {state}

        Frage des Fahrers:

        {question}
        """;
}
