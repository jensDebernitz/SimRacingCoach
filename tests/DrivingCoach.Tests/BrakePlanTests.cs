using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft, wie fest der Coach bremsen lässt und wo er das Lösen ansagt.
/// </summary>
/// <remarks>
/// Gearbeitet wird mit handgebauten Runden statt mit
/// <see cref="VirtualDriver"/>. Dessen Tempoprofil bremst mit konstanter
/// Verzögerung, der Bremskanal ist also ein Rechteck: hundert Prozent bis zum
/// Scheitel, dann null. Genau die Modulation, um die es hier geht, kommt darin
/// nicht vor – bei einem echten Fahrer dagegen immer.
/// </remarks>
public sealed class BrakePlanTests
{
    private const float BinSize = 5f;
    private const float TrackLength = 1000f;

    /// <summary>Wo in allen Testrunden gebremst wird.</summary>
    private const float BrakePoint = 200f;

    [Fact]
    public void Ohne_Referenzrunde_gibt_es_keine_Vorgabe()
    {
        Assert.Null(BrakePlan.For(reference: null, BrakePoint));
    }

    [Fact]
    public void Auf_der_Geraden_gibt_es_nichts_vorzugeben()
    {
        Assert.Null(BrakePlan.For(LapWith(_ => 0f), BrakePoint));
    }

    /// <summary>
    /// Der Spitzendruck ist gemessen, nicht geraten: Wer in der Referenz mit
    /// siebzig Prozent angebremst hat, soll nicht plötzlich hundert hören.
    /// </summary>
    [Fact]
    public void Der_Spitzendruck_stammt_aus_der_Referenz()
    {
        BrakePlan full = Plan(LapWith(TrailBrake));
        BrakePlan soft = Plan(LapWith(d => TrailBrake(d) * 0.7f));

        Assert.Equal(100, full.PeakPercent);
        Assert.Equal(70, soft.PeakPercent);
    }

    /// <summary>
    /// Der Fall aus der Anfrage: voll anbremsen, dann ein Stück vor der Kurve
    /// auf drei Viertel zurück.
    /// </summary>
    [Fact]
    public void Die_Vorgabe_nennt_einen_Loesepunkt()
    {
        BrakePlan plan = Plan(LapWith(TrailBrake));

        Assert.True(plan.HasRelease, "Aus einer ausgeprägten Löse-Rampe kommt kein Lösepunkt.");
        Assert.Equal(100, plan.PeakPercent);
        Assert.Equal(75, plan.ReleasePercent);
    }

    [Fact]
    public void Der_Loesepunkt_liegt_in_der_Bremsphase()
    {
        BrakePlan plan = Plan(LapWith(TrailBrake));

        Assert.InRange(plan.ReleaseAtMetres, 1f, plan.EndMetres - 1f);
    }

    /// <summary>
    /// Wer durchtritt und dann vom Pedal geht, hat nichts zu lösen. Die Werte
    /// müssen dann offen bleiben – eine 0 hieße "ganz aufmachen".
    /// </summary>
    [Fact]
    public void Ohne_Modulation_bleiben_die_Loese_Werte_offen()
    {
        BrakePlan plan = Plan(LapWith(BlockBrake));

        Assert.False(plan.HasRelease);
        Assert.True(float.IsNaN(plan.ReleaseAtMetres));
        Assert.True(float.IsNaN(plan.ReleaseTo));
        Assert.Equal(100, plan.PeakPercent);
    }

    /// <summary>
    /// Der Bremspunkt der Ideallinie liegt vor dem, an dem in der Referenz
    /// tatsächlich getreten wurde – das ist ja gerade der Tipp. Die Suche muss
    /// die Bremsphase trotzdem finden, sonst gäbe es mit Ideallinie nie eine
    /// Bremskraft.
    /// </summary>
    [Fact]
    public void Ein_frueherer_Bremspunkt_findet_dieselbe_Bremsphase()
    {
        RecordedLap lap = LapWith(TrailBrake);

        BrakePlan measured = Plan(lap);
        BrakePlan early = Plan(lap, BrakePoint - 50f);

        Assert.Equal(measured.PeakPercent, early.PeakPercent);
        Assert.Equal(measured.ReleasePercent, early.ReleasePercent);

        // Die Bremsphase endet an derselben Stelle der Strecke, liegt vom
        // früheren Punkt aus aber 50 m weiter weg.
        Assert.Equal(measured.EndMetres + 50f, early.EndMetres, precision: 3);
    }

    [Fact]
    public void Weit_vor_der_Bremsphase_gibt_es_noch_nichts()
    {
        Assert.Null(BrakePlan.For(LapWith(TrailBrake), BrakePoint - 130f));
    }

    [Fact]
    public void Ein_Antippen_ist_keine_Bremsphase()
    {
        Assert.Null(BrakePlan.For(LapWith(d => d is >= BrakePoint and < 260f ? 0.1f : 0f), BrakePoint));
    }

    [Fact]
    public void Eine_sehr_kurze_Bremsphase_lohnt_keine_Vorgabe()
    {
        Assert.Null(BrakePlan.For(LapWith(d => d is >= BrakePoint and < 210f ? 1f : 0f), BrakePoint));
    }

    /// <summary>
    /// Zahlen im Fünferraster. Ungerundet spränge die Anzeige bei jedem Frame
    /// um einen Punkt, und im Ohr wäre "dreiundachtzig Prozent" ohnehin nur
    /// falsche Genauigkeit.
    /// </summary>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.02f, 0)]
    [InlineData(0.03f, 5)]
    [InlineData(0.7607f, 75)]
    [InlineData(1f, 100)]
    public void Prozentangaben_liegen_auf_dem_Fuenferraster(float force, int expected)
    {
        Assert.Equal(expected, BrakePlan.Percent(force));
    }

    /// <summary>
    /// Eine kaputte Referenz darf keine unsinnige Zahl in die Ansage tragen.
    /// </summary>
    [Fact]
    public void Unsinnige_Werte_landen_im_gueltigen_Bereich()
    {
        Assert.Equal(0, BrakePlan.Percent(float.NaN));
        Assert.Equal(0, BrakePlan.Percent(-1f));
        Assert.Equal(100, BrakePlan.Percent(1.5f));
    }

    /// <summary>
    /// "Voll" statt "hundert Prozent": Im Moment des Tritts zählt jede Silbe.
    /// </summary>
    [Fact]
    public void Volle_Bremsung_heisst_voll()
    {
        Assert.Equal("Bremsen, voll", new BrakePlan(1f, float.NaN, float.NaN, 120f).CallText);
        Assert.Equal("Bremsen, 70 Prozent", new BrakePlan(0.7f, float.NaN, float.NaN, 120f).CallText);
    }

    [Fact]
    public void Der_Loeseruf_nennt_den_kleineren_Druck()
    {
        var plan = new BrakePlan(1f, 0.72f, 50f, 140f);

        Assert.Equal("Lösen auf 70", plan.ReleaseCallText);
        Assert.True(plan.ReleasePercent < plan.PeakPercent);
    }

    private static BrakePlan Plan(RecordedLap lap, float brakePoint = BrakePoint)
    {
        BrakePlan? plan = BrakePlan.For(lap, brakePoint);
        Assert.True(plan.HasValue, $"Bei {brakePoint:0} m kommt keine Bremskraft-Vorgabe heraus.");
        return plan!.Value;
    }

    /// <summary>
    /// Anbremsen mit vollem Druck ab 200 m, ab 260 m gleichmäßig aufgemacht bis
    /// 400 m – das Muster, das ein Fahrer mit Trailbraking erzeugt.
    /// </summary>
    private static float TrailBrake(float distance) => distance switch
    {
        < 200f => 0f,
        < 400f => MathF.Min(1f, 1f - ((distance - 260f) / 140f)),
        _ => 0f,
    };

    /// <summary>Durchtreten und dann vom Pedal – ohne jede Modulation.</summary>
    private static float BlockBrake(float distance) => distance is >= 200f and < 320f ? 1f : 0f;

    /// <summary>
    /// Eine Runde, deren Bremskanal genau dem übergebenen Verlauf folgt. Die
    /// übrigen Kanäle bleiben leer: Die Vorgabe liest allein die Bremse.
    /// </summary>
    private static RecordedLap LapWith(Func<float, float> brakeAt)
    {
        int bins = (int)(TrackLength / BinSize);
        LapChannels channels = LapChannels.Allocate(bins);

        for (int i = 0; i < bins; i++)
        {
            channels.Brake[i] = brakeAt(i * BinSize);
            channels.Speed[i] = 50f;
            channels.Time[i] = i * BinSize / 50f;
        }

        return new RecordedLap
        {
            ReferenceKey = "test",
            TrackName = "Teststrecke",
            CarName = "Testwagen",
            TrackLength = TrackLength,
            BinSize = BinSize,
            LapTime = TrackLength / 50f,
            RecordedAt = DateTimeOffset.UnixEpoch,
            Channels = channels,
        };
    }
}
