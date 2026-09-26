namespace DrivingCoach.Coaching.Model;

/// <summary>Eine Stellschraube beim Einmessen der Linie.</summary>
public enum CalibrationKnob
{
    FieldOfView,
    EyeHeight,
    EyeForward,
    EyeRight,
    Pitch,
}

/// <summary>
/// Führt durch das Einmessen: welcher Wert gerade dran ist, wie weit ein
/// Tastendruck ihn verstellt und wie er heißt.
/// </summary>
/// <remarks>
/// <para>
/// Die Schrittweiten sind so gewählt, dass ein Tastendruck im Bild sichtbar
/// ist, aber nicht darüber hinausschießt – wer zwanzigmal drücken muss, um
/// eine Änderung zu sehen, gibt vorher auf; wer beim ersten Druck die Linie
/// vom Bildschirm schiebt, findet nicht zurück.
/// </para>
/// <para>
/// Reihenfolge nach Wirkung: Der Blickwinkel entscheidet, ob die Linie
/// überhaupt mit der Fahrbahn zusammenläuft. Erst danach lohnt es sich, den
/// Sitz zu verschieben.
/// </para>
/// </remarks>
public sealed class CalibrationTuner
{
    private static readonly CalibrationKnob[] Order =
    [
        CalibrationKnob.FieldOfView,
        CalibrationKnob.EyeHeight,
        CalibrationKnob.EyeForward,
        CalibrationKnob.EyeRight,
        CalibrationKnob.Pitch,
    ];

    private int _index;

    /// <summary>Der Wert, der gerade verstellt wird.</summary>
    public CalibrationKnob Knob => Order[_index];

    /// <summary>Wechselt zum nächsten oder vorherigen Wert, im Kreis.</summary>
    public void Select(int direction)
    {
        _index = (_index + direction) % Order.Length;
        if (_index < 0)
        {
            _index += Order.Length;
        }
    }

    /// <summary>
    /// Verstellt den ausgewählten Wert um so viele Schritte.
    /// </summary>
    /// <remarks>
    /// Die Grenzen liegen enger als die von
    /// <see cref="CameraCalibration.IsPlausible"/>: Ein Wert, der genau auf der
    /// Grenze steht, gilt dort schon als unbrauchbar, und die Linie wäre beim
    /// Einmessen plötzlich weg.
    /// </remarks>
    public CameraCalibration Apply(CameraCalibration calibration, int steps) => Knob switch
    {
        CalibrationKnob.FieldOfView => calibration with
        {
            FieldOfViewDegrees = Clamp(calibration.FieldOfViewDegrees + (steps * 1f), 25f, 140f),
        },

        CalibrationKnob.EyeHeight => calibration with
        {
            EyeHeight = Clamp(calibration.EyeHeight + (steps * 0.02f), -0.5f, 2f),
        },

        CalibrationKnob.EyeForward => calibration with
        {
            EyeForward = Clamp(calibration.EyeForward + (steps * 0.05f), -2f, 4f),
        },

        CalibrationKnob.EyeRight => calibration with
        {
            EyeRight = Clamp(calibration.EyeRight + (steps * 0.02f), -1.5f, 1.5f),
        },

        _ => calibration with
        {
            PitchOffsetDegrees = Clamp(calibration.PitchOffsetDegrees + (steps * 0.25f), -20f, 20f),
        },
    };

    /// <summary>Der ausgewählte Wert als Text, für die Zeile im Overlay.</summary>
    public string Describe(CameraCalibration calibration) => Knob switch
    {
        CalibrationKnob.FieldOfView => $"Blickwinkel {calibration.FieldOfViewDegrees:0}°",
        CalibrationKnob.EyeHeight => $"Augenhöhe {calibration.EyeHeight:0.00} m",
        CalibrationKnob.EyeForward => $"Sitz längs {calibration.EyeForward:0.00} m",
        CalibrationKnob.EyeRight => $"Sitz seitlich {calibration.EyeRight:0.00} m",
        _ => $"Neigung {calibration.PitchOffsetDegrees:0.00}°",
    };

    /// <summary>
    /// Rundet auf die Schrittweite ab – sonst schleppt ein von Hand
    /// geschriebener Wert wie 0,5137 seine Nachkommastellen ewig mit.
    /// </summary>
    private static float Clamp(float value, float minimum, float maximum) =>
        MathF.Round(Math.Clamp(value, minimum, maximum), 3);
}
