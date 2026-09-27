using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;

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

    /// <summary>
    /// Zwei Runden: in der ersten kennt der Coach die Strecke noch nicht, ab
    /// der zweiten steht die Referenz und mit ihr die Bremspunkte.
    /// </summary>
    [Fact]
    public void Der_Bremspunkt_wird_angesagt()
    {
        Run(VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        Assert.Contains(_speech, text => text.StartsWith("Bremsen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Der Ruf nennt auch, wie fest zu treten ist. Auf der Teststrecke bremst
    /// das Tempoprofil mit voller Verzögerung, also heißt die Antwort "voll".
    /// </summary>
    [Fact]
    public void Der_Bremsruf_nennt_die_Bremskraft()
    {
        Run(VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        Assert.Contains("Bremsen, voll", _speech);
    }

    /// <summary>
    /// Das Tempoprofil der Teststrecke bremst mit konstanter Verzögerung: Der
    /// Bremskanal ist ein Rechteck, es gibt darin nichts zu lösen. Der Coach
    /// darf sich dann auch keinen Lösepunkt ausdenken – bei einem echten Fahrer
    /// mit Trailbraking sieht der Kanal anders aus.
    /// </summary>
    [Fact]
    public void Ohne_Modulation_kommt_kein_Loeseruf()
    {
        Run(VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        Assert.DoesNotContain(_messages, m => m.Kind == CoachMessageKind.BrakeRelease);
    }

    /// <summary>
    /// Der Bremsruf ist reine Ansage. Er darf keine Meldungskarte erzeugen –
    /// die stünde neben dem Countdown-Balken, der dasselbe schon sagt, und
    /// verdrängte dabei den Kurventipp aus der Liste.
    /// </summary>
    [Fact]
    public void Der_Bremsruf_steht_nicht_zusaetzlich_in_den_Meldungen()
    {
        Run(VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        CoachMessage[] calls = _messages.Where(m => m.Kind == CoachMessageKind.BrakePoint).ToArray();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Equal(string.Empty, call.Text));
    }

    /// <summary>
    /// Höchstens ein Ruf je Kurve und Runde. Ohne diese Sperre käme er bei
    /// 120 Frames je Sekunde hundertfach, solange das Auto im Anfahrfenster ist.
    /// </summary>
    [Fact]
    public void Jede_Kurve_wird_nur_einmal_pro_Runde_gerufen()
    {
        CoachEngine coach = Run(VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        int calls = _messages.Count(m => m.Kind == CoachMessageKind.BrakePoint);
        int braking = coach.State.Corners.Count(c => c.HasBrakingZone);

        Assert.InRange(calls, 1, braking);
    }

    /// <summary>
    /// Abschaltbar muss er sein: Wer den Bremspunkt im Kopf hat, will keine
    /// Stimme, die ihn jede Kurve daran erinnert.
    /// </summary>
    [Fact]
    public void Abgeschaltet_kommt_kein_Bremsruf()
    {
        Run(new CoachOptions { BrakeCallsEnabled = false }, VirtualDriver.NeutralStyles(), VirtualDriver.NeutralStyles());

        Assert.DoesNotContain(_messages, m => m.Kind == CoachMessageKind.BrakePoint);
        Assert.DoesNotContain(_messages, m => m.Kind == CoachMessageKind.BrakeRelease);
        Assert.DoesNotContain(_speech, text => text.StartsWith("Bremsen", StringComparison.Ordinal));
        Assert.DoesNotContain(_speech, text => text.StartsWith("Lösen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Der Bremsruf läuft auf einer eigenen Uhr. Auf der gemeinsamen verschluckte
    /// ihn ausgerechnet der Kurventipp, der dieselbe Kurve meint – der kommt
    /// 2,5 s vorher, der Ruf 1,2 s vorher.
    /// </summary>
    [Fact]
    public void Ein_Kurventipp_verschluckt_den_Bremsruf_nicht()
    {
        var scheduler = new SpeechScheduler(new CoachOptions());
        var frame = new TelemetryFrame { GameState = GameState.InGamePlaying };

        Assert.True(scheduler.ShouldSpeak(
            new CoachMessage(CoachMessageKind.CornerTip, "Kurve 6", "Kurve 6, später bremsen", 2, At: 10.0),
            in frame));

        Assert.True(scheduler.ShouldSpeak(
            new CoachMessage(CoachMessageKind.BrakePoint, string.Empty, "Bremsen", 3, At: 11.3),
            in frame));
    }

    [Fact]
    public void Zwei_Bremsrufe_kurz_hintereinander_werden_zu_einem()
    {
        var options = new CoachOptions();
        var scheduler = new SpeechScheduler(options);
        var frame = new TelemetryFrame { GameState = GameState.InGamePlaying };

        CoachMessage Call(double at) =>
            new(CoachMessageKind.BrakePoint, string.Empty, "Bremsen", 3, At: at);

        Assert.True(scheduler.ShouldSpeak(Call(10.0), in frame));
        Assert.False(scheduler.ShouldSpeak(Call(10.0 + options.BrakeCallMinGapSeconds - 0.5), in frame));
        Assert.True(scheduler.ShouldSpeak(Call(10.0 + options.BrakeCallMinGapSeconds + 0.1), in frame));
    }

    /// <summary>
    /// "Lösen auf 70" gehört zum "Bremsen" von eben. In einer kurzen Bremszone
    /// liegen beide keine zwei Sekunden auseinander – ausgerechnet der
    /// zugehörige Bremsruf darf den Löseruf deshalb nicht verschlucken.
    /// </summary>
    [Fact]
    public void Ein_Bremsruf_verschluckt_den_Loeseruf_nicht()
    {
        var scheduler = new SpeechScheduler(new CoachOptions());

        // Mitten im Anbremsen: Über die normale Arbeitslast-Prüfung käme hier
        // gar nichts mehr durch.
        var frame = new TelemetryFrame { GameState = GameState.InGamePlaying, Brake = 0.9f };

        Assert.True(scheduler.ShouldSpeak(
            new CoachMessage(CoachMessageKind.BrakePoint, string.Empty, "Bremsen, voll", 3, At: 10.0),
            in frame));

        Assert.True(scheduler.ShouldSpeak(
            new CoachMessage(CoachMessageKind.BrakeRelease, string.Empty, "Lösen auf 70", 3, At: 11.1),
            in frame));
    }

    [Fact]
    public void Zwei_Loeserufe_kurz_hintereinander_werden_zu_einem()
    {
        var options = new CoachOptions();
        var scheduler = new SpeechScheduler(options);
        var frame = new TelemetryFrame { GameState = GameState.InGamePlaying };

        CoachMessage Call(double at) =>
            new(CoachMessageKind.BrakeRelease, string.Empty, "Lösen auf 70", 3, At: at);

        Assert.True(scheduler.ShouldSpeak(Call(10.0), in frame));
        Assert.False(scheduler.ShouldSpeak(Call(10.0 + options.BrakeCallMinGapSeconds - 0.5), in frame));
        Assert.True(scheduler.ShouldSpeak(Call(10.0 + options.BrakeCallMinGapSeconds + 0.1), in frame));
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

    /// <summary>
    /// Eine gespeicherte Streckenkarte, die nicht mehr zur gefahrenen Runde
    /// passt, darf das Lernen nicht dauerhaft blockieren.
    /// </summary>
    /// <remarks>
    /// AMS2 meldet dieselbe Strecke gelegentlich in anderer Länge – andere
    /// Variante unter gleichem Namen, andere Boxengasse. Früher lehnte die alte
    /// Karte dann jede Runde ab, wortlos und für immer: Die Ideallinie
    /// erschien auf dieser Strecke nie wieder, und die Anzeige zählte
    /// unterdessen munter gefahrene Runden hoch.
    /// </remarks>
    [Fact]
    public void Eine_unpassende_Streckenkarte_wird_neu_begonnen()
    {
        var maps = new TrackMapStore(_directory);
        RecordedLap sample = VirtualDriver.DriveLaps(1)[0];

        TrackMap mismatched = TrackMap.Empty(
            VirtualDriver.Session.TrackKey,
            VirtualDriver.Session.TrackDisplayName,
            sample.TrackLength,
            sample.BinSize,
            sample.Channels.Count + 50);

        // Ohne Runden hielte der Speicher die Karte für unbrauchbar und gäbe
        // sie gar nicht erst heraus.
        mismatched.LapCount = TrackMap.MinimumLaps;
        maps.Save(mismatched);

        var source = new ScriptedTelemetrySource(
            VirtualDriver.Session,
            VirtualDriver.Frames(VirtualDriver.NeutralStyles(), laps: 5));

        var coach = new CoachEngine(
            source, new LapStore(_directory), new CoachOptions(), maps: maps);

        coach.MessageRaised += _messages.Add;
        coach.Start();

        TrackMap? after = maps.Load(VirtualDriver.Session.TrackKey);

        Assert.NotNull(after);
        Assert.Equal(sample.Channels.Count, after.Count);
        Assert.True(
            after.LapCount >= TrackMap.MinimumLaps,
            $"Nach fünf Runden stehen erst {after.LapCount} in der Karte – sie lernt weiterhin nichts.");

        Assert.Contains(_messages, m => m.Text.Contains("neu gelernt"));
    }

    private CoachEngine Run(params CornerStyle[][] laps) => Run(new CoachOptions(), laps);

    private CoachEngine Run(CoachOptions options, params CornerStyle[][] laps)
    {
        var source = new ScriptedTelemetrySource(VirtualDriver.Session, Stitch(laps));

        // Auch der Kartenspeicher zeigt ins Testverzeichnis. Ohne das schriebe
        // jeder Testlauf Streckenkarten in die echten Daten des angemeldeten
        // Benutzers.
        var coach = new CoachEngine(
            source, new LapStore(_directory), options, maps: new TrackMapStore(_directory));

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
