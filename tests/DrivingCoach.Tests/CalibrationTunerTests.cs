using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft das Einmessen von der Tastenseite her: welcher Wert dran ist, wie weit
/// ein Druck ihn bewegt und dass er dabei nicht aus dem Brauchbaren fällt.
/// </summary>
public class CalibrationTunerTests
{
    private static readonly CalibrationKnob[] AllKnobs = Enum.GetValues<CalibrationKnob>();

    private readonly CalibrationTuner _tuner = new();

    [Fact]
    public void Zuerst_ist_der_Blickwinkel_dran()
    {
        // Er entscheidet, ob die Linie überhaupt mit der Fahrbahn zusammenläuft.
        Assert.Equal(CalibrationKnob.FieldOfView, _tuner.Knob);
    }

    [Fact]
    public void Jeder_Wert_ist_erreichbar()
    {
        var seen = new List<CalibrationKnob>();

        for (int i = 0; i < AllKnobs.Length; i++)
        {
            seen.Add(_tuner.Knob);
            _tuner.Select(+1);
        }

        Assert.Equal(AllKnobs, seen);
    }

    [Fact]
    public void Nach_dem_letzten_Wert_geht_es_vorn_wieder_los()
    {
        for (int i = 0; i < AllKnobs.Length; i++)
        {
            _tuner.Select(+1);
        }

        Assert.Equal(CalibrationKnob.FieldOfView, _tuner.Knob);
    }

    /// <summary>
    /// Ein Schritt zurück vom ersten Wert darf nicht in einen negativen Index
    /// laufen – sonst fliegt beim ersten Druck auf Strg+Alt+← alles auseinander.
    /// </summary>
    [Fact]
    public void Rueckwaerts_vom_ersten_Wert_landet_beim_letzten()
    {
        _tuner.Select(-1);

        Assert.Equal(AllKnobs[^1], _tuner.Knob);
    }

    [Fact]
    public void Hin_und_zurueck_aendert_nichts()
    {
        _tuner.Select(+1);
        _tuner.Select(+1);
        _tuner.Select(-1);
        _tuner.Select(-1);

        Assert.Equal(CalibrationKnob.FieldOfView, _tuner.Knob);
    }

    [Fact]
    public void Ein_Druck_verstellt_den_Blickwinkel_um_ein_Grad()
    {
        CameraCalibration changed = _tuner.Apply(CameraCalibration.Default, +1);

        Assert.Equal(CameraCalibration.Default.FieldOfViewDegrees + 1f, changed.FieldOfViewDegrees);
    }

    [Fact]
    public void Ein_Druck_nach_unten_geht_den_gleichen_Weg_zurueck()
    {
        CameraCalibration up = _tuner.Apply(CameraCalibration.Default, +1);

        Assert.Equal(CameraCalibration.Default.FieldOfViewDegrees, _tuner.Apply(up, -1).FieldOfViewDegrees);
    }

    /// <summary>
    /// Jeder Wert muss sich rühren, sobald er ausgewählt ist – und nur er.
    /// </summary>
    [Theory]
    [InlineData(CalibrationKnob.FieldOfView)]
    [InlineData(CalibrationKnob.EyeHeight)]
    [InlineData(CalibrationKnob.EyeForward)]
    [InlineData(CalibrationKnob.EyeRight)]
    [InlineData(CalibrationKnob.Pitch)]
    public void Der_ausgewaehlte_Wert_bewegt_sich_und_sonst_keiner(CalibrationKnob knob)
    {
        SelectKnob(knob);

        CameraCalibration before = CameraCalibration.Default;
        CameraCalibration after = _tuner.Apply(before, +1);

        Assert.NotEqual(before, after);
        Assert.Equal(1, Changes(before, after));
    }

    /// <summary>
    /// Die Grenzen liegen enger als <see cref="CameraCalibration.IsPlausible"/>:
    /// Wer beim Einmessen dauerhaft in eine Richtung drückt, darf nicht in einen
    /// Zustand geraten, in dem die Linie kommentarlos verschwindet.
    /// </summary>
    [Theory]
    [InlineData(CalibrationKnob.FieldOfView)]
    [InlineData(CalibrationKnob.EyeHeight)]
    [InlineData(CalibrationKnob.EyeForward)]
    [InlineData(CalibrationKnob.EyeRight)]
    [InlineData(CalibrationKnob.Pitch)]
    public void Auch_am_Anschlag_bleibt_die_Kamera_brauchbar(CalibrationKnob knob)
    {
        SelectKnob(knob);

        foreach (int direction in new[] { +1, -1 })
        {
            CameraCalibration calibration = CameraCalibration.Default;

            for (int i = 0; i < 500; i++)
            {
                calibration = _tuner.Apply(calibration, direction);
            }

            Assert.True(
                calibration.IsPlausible,
                $"{knob} in Richtung {direction} landet außerhalb: {_tuner.Describe(calibration)}");
        }
    }

    [Fact]
    public void Am_Anschlag_bewegt_sich_nichts_mehr()
    {
        CameraCalibration stopped = CameraCalibration.Default;

        for (int i = 0; i < 500; i++)
        {
            stopped = _tuner.Apply(stopped, +1);
        }

        Assert.Equal(stopped, _tuner.Apply(stopped, +1));
    }

    /// <summary>
    /// Krumme Nachkommastellen dürfen sich nicht durch die Sitzung schleppen –
    /// im Overlay stünde sonst irgendwann eine Zahl, die niemand wiedererkennt.
    /// </summary>
    [Fact]
    public void Verstellte_Werte_bleiben_rund()
    {
        SelectKnob(CalibrationKnob.EyeHeight);

        CameraCalibration crooked = CameraCalibration.Default with { EyeHeight = 0.513_7f };

        Assert.Equal(0.534, _tuner.Apply(crooked, +1).EyeHeight, 3);
    }

    [Theory]
    [InlineData(CalibrationKnob.FieldOfView, "Blickwinkel")]
    [InlineData(CalibrationKnob.EyeHeight, "Augenhöhe")]
    [InlineData(CalibrationKnob.EyeForward, "Sitz längs")]
    [InlineData(CalibrationKnob.EyeRight, "Sitz seitlich")]
    [InlineData(CalibrationKnob.Pitch, "Neigung")]
    public void Der_Text_nennt_Wert_und_Zahl(CalibrationKnob knob, string expected)
    {
        SelectKnob(knob);

        string text = _tuner.Describe(CameraCalibration.Default);

        Assert.StartsWith(expected, text);
        Assert.True(text.Any(char.IsDigit), $"Ohne Zahl sagt \"{text}\" nichts darüber, wo der Wert gerade steht.");
    }

    private void SelectKnob(CalibrationKnob knob)
    {
        while (_tuner.Knob != knob)
        {
            _tuner.Select(+1);
        }
    }

    /// <summary>Wie viele der fünf Stellschrauben sich unterscheiden.</summary>
    private static int Changes(CameraCalibration before, CameraCalibration after) =>
        new[]
        {
            before.FieldOfViewDegrees != after.FieldOfViewDegrees,
            before.EyeHeight != after.EyeHeight,
            before.EyeForward != after.EyeForward,
            before.EyeRight != after.EyeRight,
            before.PitchOffsetDegrees != after.PitchOffsetDegrees,
        }.Count(changed => changed);
}
