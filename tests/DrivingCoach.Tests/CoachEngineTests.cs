using DrivingCoach.Coaching;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Tests;

/// <summary>
/// Spielt fertige Frames ab, als kämen sie aus AMS2. Damit lässt sich die
/// <see cref="CoachEngine"/> in Millisekunden prüfen – der echte Simulator
/// bräuchte für drei Runden ein paar Minuten Echtzeit.
/// </summary>
internal sealed class ScriptedTelemetrySource(SessionInfo session, IReadOnlyList<TelemetryFrame> frames)
    : ITelemetrySource
{
    public event Action<TelemetrySnapshot>? FrameReceived;

    public event Action<SessionInfo>? SessionChanged;

    public event Action<TelemetryStatus>? StatusChanged;

    public TelemetryStatus Status { get; } = new(true, "Skript");

    public void Start()
    {
        StatusChanged?.Invoke(Status);
        SessionChanged?.Invoke(session);

        foreach (TelemetryFrame frame in frames)
        {
            FrameReceived?.Invoke(new TelemetrySnapshot(session, frame));
        }
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Prüft das Zusammenspiel, das im Overlay hängt: Aufzeichnung, Referenz,
/// Analyse, Meldungen und Ansagen über mehrere Runden hinweg.
/// </summary>
public sealed class CoachEngineTests : IDisposable
{
    /// <summary>Kurve, in der der virtuelle Fahrer ab Runde 2 Zeit liegen lässt.</summary>
    private const int MistakeIndex = 5;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DrivingCoachTests",
        Guid.NewGuid().ToString("N"));

    private readonly List<CoachMessage> _messages = [];
    private readonly List<string> _speech = [];

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Erste_gueltige_Runde_wird_zur_Referenz()
    {
        Run(VirtualDriver.NeutralStyles());

        Assert.Contains(
            _messages,
            m => m.Kind == CoachMessageKind.Achievement && m.Text.Contains("Erste Referenzrunde"));
    }

    [Fact]
    public void Ohne_Referenz_fordert_der_Coach_eine_saubere_Runde()
    {
        Run(VirtualDriver.NeutralStyles());

        CoachMessage first = _messages[0];

        Assert.Equal(CoachMessageKind.Info, first.Kind);
        Assert.Contains("keine Referenz", first.Text);
        Assert.Contains("Fahr eine saubere Runde", first.SpeechText);
    }

    [Fact]
    public void Zweite_Runde_wird_gegen_die_Referenz_ausgewertet()
    {
        CoachEngine coach = Run(VirtualDriver.NeutralStyles(), Mistake());

        Assert.NotNull(coach.State.LastAnalysis);
        Assert.True(
            coach.State.LastAnalysis!.LapDelta > 0.1f,
            $"Die Fehlerrunde hätte langsamer sein müssen, war aber {coach.State.LastAnalysis.LapDelta:0.000} s.");

        Assert.Contains(_messages, m => m.Kind == CoachMessageKind.LapSummary);
    }

    [Fact]
    public void Rundenbericht_nennt_die_teuerste_Kurve()
    {
        Run(VirtualDriver.NeutralStyles(), Mistake());

        string cornerName = $"Kurve {MistakeIndex + 1}";

        Assert.Contains(
            _messages,
            m => m.Kind == CoachMessageKind.LapSummary
                && m.Text.StartsWith("Größter Verlust", StringComparison.Ordinal)
                && m.Text.Contains(cornerName));
    }

    [Fact]
    public void Der_Fehler_der_Vorrunde_wird_vor_der_Kurve_angesagt()
    {
        // Drei Runden: aufzeichnen, Fehler machen, Fehler wiederholen. Erst in
        // der dritten Runde kann der Coach vorwarnen – vorher kennt er ihn nicht.
        Run(VirtualDriver.NeutralStyles(), Mistake(), Mistake());

        CoachMessage[] tips = _messages.Where(m => m.Kind == CoachMessageKind.CornerTip).ToArray();

        Assert.NotEmpty(tips);
        Assert.Contains(tips, t => t.Text.Contains($"Kurve {MistakeIndex + 1}"));
    }

    [Fact]
    public void Jede_Ansage_hat_einen_Text()
    {
        Run(VirtualDriver.NeutralStyles(), Mistake(), Mistake());

        Assert.NotEmpty(_speech);
        Assert.All(_speech, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }

    [Fact]
    public void Gefahrene_Runden_werden_gezaehlt()
    {
        CoachEngine coach = Run(VirtualDriver.NeutralStyles(), Mistake(), Mistake());

        Assert.Equal(3, coach.State.ValidLapCount);
    }

    [Fact]
    public void Referenz_verwerfen_loescht_Runde_und_Kurven()
    {
        CoachEngine coach = Run(VirtualDriver.NeutralStyles());

        Assert.NotNull(coach.State.Reference);
        Assert.NotEmpty(coach.State.Corners);

        coach.ResetReference();

        Assert.Null(coach.State.Reference);
        Assert.Empty(coach.State.Corners);
        Assert.Contains(_messages, m => m.Text.Contains("Referenzrunde gelöscht"));
    }

    /// <summary>Eine deutlich zu langsam genommene Kurve.</summary>
    private static CornerStyle[] Mistake()
    {
        var styles = VirtualDriver.NeutralStyles();
        styles[MistakeIndex] = new CornerStyle(SpeedScale: 0.80f);
        return styles;
    }

    private CoachEngine Run(params CornerStyle[][] laps)
    {
        var source = new ScriptedTelemetrySource(VirtualDriver.Session, Stitch(laps));
        var coach = new CoachEngine(source, new LapStore(_directory), new CoachOptions());

        coach.MessageRaised += _messages.Add;
        coach.SpeechRequested += _speech.Add;

        coach.Start();

        return coach;
    }

    /// <summary>
    /// Näht mehrere Einzelrunden zu einer durchgehenden Fahrt zusammen.
    /// </summary>
    /// <remarks>
    /// <see cref="VirtualDriver.Frames"/> kann pro Aufruf nur einen Fahrstil
    /// abbilden. Für "Runde 1 sauber, Runde 2 mit Fehler" müssen die Läufe
    /// deshalb einzeln erzeugt und anschließend in Zeitstempel und Rundenzähler
    /// fortgeschrieben werden – sonst sähe der Recorder lauter erste Runden.
    /// </remarks>
    private static List<TelemetryFrame> Stitch(IReadOnlyList<CornerStyle[]> laps)
    {
        var all = new List<TelemetryFrame>();

        double timeOffset = 0.0;
        float lastLapTime = 0f;

        for (int lap = 0; lap < laps.Count; lap++)
        {
            List<TelemetryFrame> single = VirtualDriver.Frames(laps[lap], laps: 1);

            // Der letzte Frame gehört bereits zur Folgerunde und wird von der
            // nächsten Iteration selbst erzeugt.
            for (int i = 0; i < single.Count - 1; i++)
            {
                TelemetryFrame frame = single[i];

                all.Add(frame with
                {
                    Timestamp = timeOffset + frame.Timestamp,
                    LapsCompleted = lap,
                    CurrentLap = lap + 1,
                    LastLapTime = lastLapTime,
                    BestLapTime = lastLapTime,
                });
            }

            lastLapTime = single[^2].CurrentLapTime;
            timeOffset += single[^1].Timestamp;
        }

        // Ein Frame hinter der Ziellinie, damit die letzte Runde abgeschlossen wird.
        all.Add(all[^1] with
        {
            Timestamp = timeOffset,
            LapDistance = 0f,
            CurrentLapTime = 0f,
            LapsCompleted = laps.Count,
            CurrentLap = laps.Count + 1,
            LastLapTime = lastLapTime,
            BestLapTime = lastLapTime,
        });

        return all;
    }
}
