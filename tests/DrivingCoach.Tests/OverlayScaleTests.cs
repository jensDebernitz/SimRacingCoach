using DrivingCoach.Overlay;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft die Rechnung hinter Strg+Alt++ und Strg+Alt+−.
/// </summary>
public sealed class OverlayScaleTests
{
    [Fact]
    public void Hundert_Prozent_bleiben_hundert_Prozent()
    {
        Assert.Equal(1.0, OverlayScale.Clamp(1.0), precision: 6);
        Assert.Equal(100, OverlayScale.Percent(1.0));
    }

    /// <summary>
    /// Jeder Wert liegt auf dem Raster. Ohne das Runden summierten sich die
    /// Fließkommareste jedes Tastendrucks, bis "130 %" irgendwann 129 % hieße.
    /// </summary>
    [Fact]
    public void Jede_Groesse_liegt_auf_dem_Raster()
    {
        double scale = OverlayScale.Minimum;

        for (int i = 0; i < 40; i++)
        {
            scale = OverlayScale.Clamp(scale + OverlayScale.Step);

            double steps = scale / OverlayScale.Step;
            Assert.Equal(Math.Round(steps), steps, precision: 6);
        }
    }

    /// <summary>
    /// Zwanzig Schritte nach oben und zwanzig zurück müssen wieder am Anfang
    /// landen – sonst wandert die Größe bei jedem Hin und Her.
    /// </summary>
    [Fact]
    public void Hoch_und_wieder_runter_landet_beim_Ausgangswert()
    {
        const double start = 1.0;
        double scale = start;

        for (int i = 0; i < 5; i++)
        {
            scale = OverlayScale.Clamp(scale + OverlayScale.Step);
        }

        for (int i = 0; i < 5; i++)
        {
            scale = OverlayScale.Clamp(scale - OverlayScale.Step);
        }

        Assert.Equal(start, scale, precision: 6);
    }

    [Fact]
    public void Ueber_den_Anschlag_hinaus_geht_es_nicht()
    {
        Assert.Equal(OverlayScale.Maximum, OverlayScale.Clamp(99.0), precision: 6);
        Assert.Equal(OverlayScale.Minimum, OverlayScale.Clamp(0.01), precision: 6);
    }

    /// <summary>
    /// Eine kaputte Einstellungsdatei darf das Overlay nicht auf Größe null
    /// oder unendlich ziehen – von dort käme man ohne Texteditor nicht zurück.
    /// </summary>
    [Fact]
    public void Unsinnige_Werte_landen_bei_hundert_Prozent()
    {
        Assert.Equal(1.0, OverlayScale.Clamp(double.NaN), precision: 6);
        Assert.Equal(1.0, OverlayScale.Clamp(double.PositiveInfinity), precision: 6);
        Assert.Equal(1.0, OverlayScale.Clamp(double.NegativeInfinity), precision: 6);
    }

    /// <summary>
    /// Der Grund für die automatische Vorgabe: Auf einem hohen Bildschirm ist
    /// 100 % zu klein, und niemand sucht eine Taste für ein Problem, das er für
    /// beabsichtigt hält.
    /// </summary>
    [Theory]
    [InlineData(1080.0, 1.0)]
    [InlineData(1440.0, 1.3)]
    [InlineData(2160.0, 2.0)]
    public void Der_Bildschirm_gibt_die_Startgroesse_vor(double screenHeight, double expected)
    {
        Assert.Equal(expected, OverlayScale.ForScreen(screenHeight), precision: 6);
    }

    /// <summary>
    /// Ein 34-Zoll-Ultrawide ist breit, nicht hoch: 3440×1440. Über die Breite
    /// gerechnet käme fast 180 % heraus – viel zu groß. Die Höhe ist das
    /// richtige Maß, weil daran hängt, wie hoch eine Zeile im Blickfeld steht.
    /// </summary>
    [Fact]
    public void Der_Ultrawide_bekommt_keine_uebertriebene_Groesse()
    {
        Assert.Equal(1.3, OverlayScale.ForScreen(1440.0), precision: 6);
    }

    /// <summary>
    /// Kleiner als 100 % stellt die Automatik nie ein. Wer einen niedrigen
    /// Bildschirm hat, hat schon wenig Platz; ihm auch noch die Schrift zu
    /// verkleinern hülfe niemandem.
    /// </summary>
    [Fact]
    public void Kleiner_als_hundert_Prozent_faengt_niemand_an()
    {
        Assert.Equal(1.0, OverlayScale.ForScreen(768.0), precision: 6);
    }

    /// <summary>
    /// Vor dem ersten Zeichnen kennt WPF die Bildschirmhöhe unter Umständen
    /// noch nicht.
    /// </summary>
    [Fact]
    public void Ohne_bekannten_Bildschirm_bleibt_es_bei_hundert_Prozent()
    {
        Assert.Equal(1.0, OverlayScale.ForScreen(0.0), precision: 6);
        Assert.Equal(1.0, OverlayScale.ForScreen(-1.0), precision: 6);
    }

    /// <summary>
    /// Die Automatik muss durch den Begrenzer kommen, ohne sich zu verändern –
    /// sonst zeigte die Rückmeldung nach dem Start eine andere Zahl an, als das
    /// Fenster tatsächlich groß ist.
    /// </summary>
    [Theory]
    [InlineData(1080.0)]
    [InlineData(1440.0)]
    [InlineData(2160.0)]
    public void Die_Startgroesse_uebersteht_den_Begrenzer(double screenHeight)
    {
        double automatic = OverlayScale.ForScreen(screenHeight);

        Assert.Equal(automatic, OverlayScale.Clamp(automatic), precision: 6);
    }

    [Fact]
    public void Die_Prozentanzeige_rundet_auf_ganze_Zahlen()
    {
        Assert.Equal(130, OverlayScale.Percent(1.3));
        Assert.Equal(250, OverlayScale.Percent(OverlayScale.Maximum));
        Assert.Equal(80, OverlayScale.Percent(OverlayScale.Minimum));
    }
}
