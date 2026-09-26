namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Wie der Gierwinkel aus dem Spiel in eine Blickrichtung in
/// Streckenkoordinaten umzurechnen ist.
/// </summary>
/// <param name="Sign">+1 oder -1, je nach Drehsinn des Spiels.</param>
/// <param name="OffsetRadians">Wo bei diesem Winkel "geradeaus" liegt.</param>
/// <remarks>
/// <para>
/// Der Shared Memory nennt einen Gierwinkel, aber nirgends, wie er gemeint
/// ist: Ob er im oder gegen den Uhrzeigersinn zählt und wo sein Nullpunkt
/// liegt, steht in keiner Dokumentation. Falsch geraten heißt: die
/// perspektivische Linie liegt spiegelverkehrt auf der Strecke.
/// </para>
/// <para>
/// Statt zu raten lässt es sich ausrechnen. Auf einer Geraden bei hohem Tempo
/// zeigt das Auto genau dorthin, wo es hinfährt – und wohin es gefahren ist,
/// steht als Folge von Positionen längst in der Runde. Der Vergleich beider
/// liefert Drehsinn und Nullpunkt.
/// </para>
/// </remarks>
public readonly record struct PoseConvention(float Sign, float OffsetRadians)
{
    /// <summary>
    /// Die Annahme, mit der gerechnet wird, solange nichts gelernt wurde:
    /// Winkel im Uhrzeigersinn, Null zeigt nach +Z.
    /// </summary>
    public static readonly PoseConvention Assumed = new(1f, 0f);

    /// <summary>Nur auf einer Geraden stimmen Blickrichtung und Fahrtrichtung überein.</summary>
    private const float MinSpeed = 20f;

    /// <summary>Mittlere Abweichung in Radiant, ab der das Ergebnis verworfen wird (rund 9°).</summary>
    private const float MaxMeanError = 0.16f;

    /// <summary>Rechnet den rohen Winkel in eine Blickrichtung um.</summary>
    public float HeadingFrom(float yaw) => Normalize((Sign * yaw) + OffsetRadians);

    /// <summary>Richtungsvektor in der Ebene, X und Z.</summary>
    public (float X, float Z) DirectionFrom(float yaw)
    {
        float heading = HeadingFrom(yaw);
        return (MathF.Sin(heading), MathF.Cos(heading));
    }

    /// <summary>
    /// Leitet die Konvention aus einer gefahrenen Runde ab.
    /// </summary>
    /// <returns><c>null</c>, wenn die Runde dafür nicht taugt.</returns>
    public static PoseConvention? Learn(RecordedLap lap)
    {
        LapChannels channels = lap.Channels;
        if (!channels.HasGeometry || channels.Yaw.Length != channels.Count)
        {
            return null;
        }

        var samples = new List<(float Travel, float Yaw)>(channels.Count);

        for (int bin = 0; bin < channels.Count; bin++)
        {
            if (channels.Speed[bin] < MinSpeed)
            {
                continue;
            }

            // Nur auf der Geraden: In einer Kurve dreht sich das Auto
            // zwischen zwei Abschnitten weiter, und im Grenzbereich steht es
            // ohnehin schräg zur Fahrtrichtung.
            if (MathF.Abs(channels.YawRate[bin]) > 0.08f)
            {
                continue;
            }

            int next = (bin + 1) % channels.Count;
            var from = channels.PositionOf(bin);
            var to = channels.PositionOf(next);

            if (from.FlatDistanceTo(to) < lap.BinSize * 0.5f)
            {
                continue;
            }

            samples.Add((MathF.Atan2(to.X - from.X, to.Z - from.Z), channels.Yaw[bin]));
        }

        if (samples.Count < 20)
        {
            return null;
        }

        // Beide Drehrichtungen durchprobieren und die nehmen, bei der die
        // Abweichung über die ganze Runde am kleinsten bleibt.
        PoseConvention best = Assumed;
        float bestError = float.MaxValue;

        foreach (float sign in (float[])[1f, -1f])
        {
            float offset = MeanAngle(samples.Select(s => Normalize(s.Travel - (sign * s.Yaw))));
            var candidate = new PoseConvention(sign, offset);
            float error = samples.Sum(s => MathF.Abs(Normalize(candidate.HeadingFrom(s.Yaw) - s.Travel)));

            if (error < bestError)
            {
                bestError = error;
                best = candidate;
            }
        }

        // Ein paar Grad Abweichung sind normal: Das Auto steht auch geradeaus
        // leicht schräg zur Fahrtrichtung, und wer aus einer Kurve kommt, zieht
        // noch quer zur Fahrbahn. Wird daraus deutlich mehr, passt keine der
        // beiden Annahmen – dann lieber gar keine Aussage als eine falsche.
        return bestError / samples.Count > MaxMeanError ? null : best;
    }

    /// <summary>Mittelwert von Winkeln – über Sinus und Kosinus, sonst springt er bei ±180°.</summary>
    private static float MeanAngle(IEnumerable<float> angles)
    {
        float sin = 0f;
        float cos = 0f;

        foreach (float angle in angles)
        {
            sin += MathF.Sin(angle);
            cos += MathF.Cos(angle);
        }

        return MathF.Atan2(sin, cos);
    }

    /// <summary>Winkel auf -π bis π bringen.</summary>
    private static float Normalize(float angle)
    {
        float wrapped = angle % (2f * MathF.PI);
        if (wrapped > MathF.PI)
        {
            wrapped -= 2f * MathF.PI;
        }
        else if (wrapped < -MathF.PI)
        {
            wrapped += 2f * MathF.PI;
        }

        return wrapped;
    }
}
