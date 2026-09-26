using DrivingCoach.Coaching;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Tests;

public class SpeechSchedulerTests
{
    private static readonly CoachOptions Options = new();

    private static TelemetryFrame Relaxed => new()
    {
        GameState = GameState.InGamePlaying,
        Speed = 60f,
        Throttle = 1f,
        Brake = 0f,
        Steering = 0f,
    };

    private static TelemetryFrame Busy => Relaxed with { Brake = 0.9f, Steering = 0.8f };

    private static CoachMessage Tip(string text, double at) =>
        new(CoachMessageKind.CornerTip, text, text, Priority: 2, At: at);

    [Fact]
    public void Spricht_eine_einzelne_Ansage()
    {
        var scheduler = new SpeechScheduler(Options);

        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 3: früher aufs Gas.", 10.0), Relaxed));
    }

    [Fact]
    public void Haelt_den_Mindestabstand_zwischen_Ansagen_ein()
    {
        var scheduler = new SpeechScheduler(Options);

        Assert.True(scheduler.ShouldSpeak(Tip("Erster Hinweis.", 10.0), Relaxed));
        Assert.False(scheduler.ShouldSpeak(Tip("Zweiter Hinweis.", 11.0), Relaxed));
        Assert.True(scheduler.ShouldSpeak(Tip("Zweiter Hinweis.", 10.0 + Options.MinimumSpeechGapSeconds), Relaxed));
    }

    [Fact]
    public void Wiederholt_denselben_Satz_nicht()
    {
        var scheduler = new SpeechScheduler(Options);

        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 10.0), Relaxed));

        // Abstand eingehalten, Text aber identisch.
        Assert.False(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 25.0), Relaxed));
        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 31.0), Relaxed));
    }

    [Fact]
    public void Schweigt_waehrend_der_Fahrer_beschaeftigt_ist()
    {
        var scheduler = new SpeechScheduler(Options);

        Assert.False(scheduler.ShouldSpeak(Tip("Kurve 4: mehr Tempo am Scheitelpunkt.", 10.0), Busy));
        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 4: mehr Tempo am Scheitelpunkt.", 10.5), Relaxed));
    }

    [Fact]
    public void Fahrfehler_kommen_auch_mitten_im_Anbremsen_durch()
    {
        var scheduler = new SpeechScheduler(Options);
        var fault = new CoachMessage(CoachMessageKind.Fault, "Vorderrad blockiert", "Vorderrad blockiert.", 4, 10.0);

        Assert.True(scheduler.ShouldSpeak(fault, Busy));

        // Und unmittelbar danach darf ein zweiter Fehler gemeldet werden.
        var second = new CoachMessage(CoachMessageKind.Fault, "Räder drehen durch", "Zu viel Gas.", 4, 10.3);
        Assert.True(scheduler.ShouldSpeak(second, Busy));
    }

    [Fact]
    public void Leere_Sprachtexte_werden_nie_gesprochen()
    {
        var scheduler = new SpeechScheduler(Options);
        var silent = new CoachMessage(CoachMessageKind.LapSummary, "Kurve 2: passt", string.Empty, 1, 10.0);

        Assert.False(scheduler.ShouldSpeak(silent, Relaxed));
    }

    [Fact]
    public void Abgeschaltete_Sprachausgabe_bleibt_stumm()
    {
        var scheduler = new SpeechScheduler(new CoachOptions { SpeechEnabled = false });

        Assert.False(scheduler.ShouldSpeak(Tip("Kurve 1: früher aufs Gas.", 10.0), Relaxed));
    }

    [Fact]
    public void Reset_hebt_alle_Sperren_auf()
    {
        var scheduler = new SpeechScheduler(Options);

        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 10.0), Relaxed));
        Assert.False(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 12.0), Relaxed));

        scheduler.Reset();

        Assert.True(scheduler.ShouldSpeak(Tip("Kurve 1: du bremst zu früh.", 12.0), Relaxed));
    }
}
