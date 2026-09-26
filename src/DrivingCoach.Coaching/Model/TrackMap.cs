using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Der befahrbare Bereich an einem Streckenpunkt, quer zur Fahrtrichtung.
/// </summary>
/// <param name="Left">Linker Rand in Metern, negativ.</param>
/// <param name="Right">Rechter Rand in Metern, positiv.</param>
public readonly record struct Corridor(float Left, float Right)
{
    /// <summary>Nutzbare Breite in Metern.</summary>
    public float Width => Right - Left;

    /// <summary>Begrenzt einen Versatz auf den Korridor.</summary>
    public float Clamp(float offset) => Math.Clamp(offset, Left, Right);
}

/// <summary>
/// Was der Coach über den Verlauf einer Strecke gelernt hat: eine Mittellinie
/// in Weltkoordinaten und die Breite, die dort zur Verfügung steht.
/// </summary>
/// <remarks>
/// <para>
/// Die Schnittstelle von AMS2 liefert keinerlei Streckengeometrie – keine
/// Ränder, keine Mittellinie, nicht einmal die Breite. Alles, was hier steht,
/// stammt aus gefahrenen Runden: die Linie als Mittel über alle Runden, die
/// Ränder aus den Rädern, die dabei ins Gras geraten sind.
/// </para>
/// <para>
/// Die Karte wird deshalb mit jeder Runde besser und ist nach der ersten Runde
/// noch fast nichts wert. <see cref="IsUsable"/> zieht die Grenze.
/// </para>
/// </remarks>
public sealed class TrackMap
{
    /// <summary>Halbe Fahrzeugbreite in Metern. Grob, aber für alle Klassen brauchbar.</summary>
    public const float CarHalfWidth = 0.9f;

    /// <summary>
    /// Wie weit der Korridor über das hinausreicht, was schon befahren wurde.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die naheliegende Lösung – eine feste Fahrbahnbreite annehmen – geht
    /// schief: Wer sich am Scheitel innen anlegt, bekäme rechnerisch noch
    /// einmal mehrere Meter Wiese dazu, und die Ideallinie würde mitten
    /// hindurchführen.
    /// </para>
    /// <para>
    /// Deshalb wächst der Korridor mit dem, was tatsächlich gefahren wurde,
    /// plus diesem Zuschlag. Der Coach darf damit etwas mehr Breite vorschlagen
    /// als je genutzt wurde, aber nicht beliebig viel.
    /// </para>
    /// </remarks>
    public const float ExplorationMargin = 1.5f;

    /// <summary>Ab so vielen Runden taugt die Karte zum Rechnen.</summary>
    public const int MinimumLaps = 3;

    public required string TrackKey { get; init; }

    public required string TrackName { get; init; }

    /// <summary>Streckenlänge in Metern.</summary>
    public required float TrackLength { get; init; }

    /// <summary>Abschnittsbreite, identisch mit der der aufgezeichneten Runden.</summary>
    public required float BinSize { get; init; }

    /// <summary>Wie viele Runden in die Karte eingeflossen sind.</summary>
    public int LapCount { get; set; }

    /// <summary>Mittellinie in Weltkoordinaten, ein Punkt je Abschnitt.</summary>
    public required float[] CentreX { get; init; }

    /// <inheritdoc cref="CentreX"/>
    public required float[] CentreY { get; init; }

    /// <inheritdoc cref="CentreX"/>
    public required float[] CentreZ { get; init; }

    /// <summary>Linker Rand als Versatz zur Mittellinie, negativ.</summary>
    public required float[] EdgeLeft { get; init; }

    /// <summary>Rechter Rand als Versatz zur Mittellinie, positiv.</summary>
    public required float[] EdgeRight { get; init; }

    public int Count => CentreX.Length;

    /// <summary>True, wenn genug Runden eingeflossen sind, um darauf zu rechnen.</summary>
    public bool IsUsable => LapCount >= MinimumLaps && Count > 0;

    /// <summary>Legt eine leere Karte passend zu einer aufgezeichneten Runde an.</summary>
    public static TrackMap Empty(string trackKey, string trackName, float trackLength, float binSize, int bins) =>
        new()
        {
            TrackKey = trackKey,
            TrackName = trackName,
            TrackLength = trackLength,
            BinSize = binSize,
            CentreX = new float[bins],
            CentreY = new float[bins],
            CentreZ = new float[bins],
            EdgeLeft = new float[bins],
            EdgeRight = new float[bins],
        };

    /// <summary>
    /// Eine eingefrorene Kopie.
    /// </summary>
    /// <remarks>
    /// Die Ideallinie wird nebenher gerechnet, während der Telemetriethread
    /// schon die nächste Runde einarbeiten könnte. Der Löser bekommt deshalb
    /// seine eigene Kopie statt der lebenden Karte.
    /// </remarks>
    public TrackMap Snapshot() => new()
    {
        TrackKey = TrackKey,
        TrackName = TrackName,
        TrackLength = TrackLength,
        BinSize = BinSize,
        LapCount = LapCount,
        CentreX = (float[])CentreX.Clone(),
        CentreY = (float[])CentreY.Clone(),
        CentreZ = (float[])CentreZ.Clone(),
        EdgeLeft = (float[])EdgeLeft.Clone(),
        EdgeRight = (float[])EdgeRight.Clone(),
    };

    public WorldPoint CentreAt(int bin) => new(CentreX[bin], CentreY[bin], CentreZ[bin]);

    public Corridor CorridorAt(int bin) => new(EdgeLeft[bin], EdgeRight[bin]);

    /// <summary>
    /// Einheitsvektor quer zur Mittellinie, nach rechts in Fahrtrichtung.
    /// </summary>
    /// <remarks>
    /// Die Richtung stammt aus den Nachbarpunkten statt aus der Fahrzeuglage:
    /// Die Karte soll auch dann noch stimmen, wenn das Auto beim Aufzeichnen
    /// quer stand.
    /// </remarks>
    public (float X, float Z) RightNormalAt(int bin)
    {
        int next = (bin + 1) % Count;
        int previous = (bin - 1 + Count) % Count;

        float tx = CentreX[next] - CentreX[previous];
        float tz = CentreZ[next] - CentreZ[previous];
        float length = MathF.Sqrt((tx * tx) + (tz * tz));

        // Zwei identische Nachbarpunkte hätten keine Richtung. Kommt bei
        // stehendem Auto vor und darf nicht durch Null teilen.
        return length < 1e-4f ? (1f, 0f) : (tz / length, -tx / length);
    }

    /// <summary>Versatz eines Punktes zur Mittellinie, positiv nach rechts.</summary>
    public float OffsetOf(int bin, WorldPoint point)
    {
        (float nx, float nz) = RightNormalAt(bin);
        return ((point.X - CentreX[bin]) * nx) + ((point.Z - CentreZ[bin]) * nz);
    }

    /// <summary>Punkt neben der Mittellinie, <paramref name="offset"/> nach rechts.</summary>
    public WorldPoint PointAt(int bin, float offset)
    {
        (float nx, float nz) = RightNormalAt(bin);
        return new WorldPoint(
            CentreX[bin] + (nx * offset),
            CentreY[bin],
            CentreZ[bin] + (nz * offset));
    }

    /// <summary>
    /// Arbeitet eine gefahrene Runde in die Karte ein.
    /// </summary>
    /// <returns>False, wenn die Runde nicht zur Karte passt oder keine Geometrie mitbringt.</returns>
    public bool Learn(RecordedLap lap)
    {
        LapChannels channels = lap.Channels;

        if (!channels.HasGeometry || channels.Count != Count || Math.Abs(lap.BinSize - BinSize) > 0.01f)
        {
            return false;
        }

        if (LapCount == 0)
        {
            Seed(lap);
            return true;
        }

        // Erst alle Versätze gegen die unveränderte Linie messen. Würde die
        // Linie schon währenddessen wandern, läse jeder Abschnitt seine
        // Querrichtung teils aus altem, teils aus neuem Verlauf.
        var offsets = new float[Count];
        for (int bin = 0; bin < Count; bin++)
        {
            offsets[bin] = OffsetOf(bin, channels.PositionOf(bin));
        }

        // Neuer Mittelwert: die alte Linie zählt so oft, wie Runden eingeflossen
        // sind, die neue Runde einmal.
        float weight = 1f / (LapCount + 1);

        for (int bin = 0; bin < Count; bin++)
        {
            WorldPoint driven = channels.PositionOf(bin);
            Widen(bin, offsets[bin], channels.OffTrackWheels[bin] > 0);

            // Die Mittellinie wandert um diesen Betrag zur Seite. Die Ränder
            // hängen an ihr, also müssen sie mitwandern – sonst würde der
            // Korridor mit jeder Runde in eine Richtung davonkriechen.
            float shift = offsets[bin] * weight;
            EdgeLeft[bin] -= shift;
            EdgeRight[bin] -= shift;

            CentreX[bin] += (driven.X - CentreX[bin]) * weight;
            CentreY[bin] += (driven.Y - CentreY[bin]) * weight;
            CentreZ[bin] += (driven.Z - CentreZ[bin]) * weight;

            Sanitize(bin);
        }

        LapCount++;
        return true;
    }

    /// <summary>Die erste Runde ist die Mittellinie; die Breite ist noch geraten.</summary>
    private void Seed(RecordedLap lap)
    {
        LapChannels channels = lap.Channels;

        for (int bin = 0; bin < Count; bin++)
        {
            WorldPoint driven = channels.PositionOf(bin);
            CentreX[bin] = driven.X;
            CentreY[bin] = driven.Y;
            CentreZ[bin] = driven.Z;
        }

        for (int bin = 0; bin < Count; bin++)
        {
            // Wo schon in der ersten Runde ein Rad daneben war, ist die
            // Strecke auf dieser Seite zu Ende.
            bool wheelsOff = channels.OffTrackWheels[bin] > 0;
            float half = wheelsOff ? CarHalfWidth : CarHalfWidth + ExplorationMargin;
            EdgeLeft[bin] = -half;
            EdgeRight[bin] = half;
        }

        LapCount = 1;
    }

    /// <summary>
    /// Zieht die Ränder an einer Stelle nach – nach außen, wenn dort gefahren
    /// wurde, nach innen, wenn dabei ein Rad daneben lief.
    /// </summary>
    private void Widen(int bin, float offset, bool wheelsOff)
    {
        if (wheelsOff)
        {
            // Hier hört der Asphalt auf. Das ist die einzige harte Information
            // über die Streckenbreite, die die Schnittstelle hergibt.
            if (offset > 0f)
            {
                EdgeRight[bin] = MathF.Min(EdgeRight[bin], offset);
            }
            else
            {
                EdgeLeft[bin] = MathF.Max(EdgeLeft[bin], offset);
            }

            return;
        }

        // Sauber gefahren heißt: bis zum äußeren Rad trägt die Strecke
        // nachweislich, und ein Stück darüber hinaus vermutlich auch.
        EdgeRight[bin] = MathF.Max(EdgeRight[bin], offset + CarHalfWidth + ExplorationMargin);
        EdgeLeft[bin] = MathF.Min(EdgeLeft[bin], offset - CarHalfWidth - ExplorationMargin);
    }

    /// <summary>
    /// Hält den Korridor plausibel. Ohne das könnte ein einzelner Ausrutscher
    /// ihn auf null zusammenziehen und die Ideallinie hätte keinen Platz mehr.
    /// </summary>
    private void Sanitize(int bin)
    {
        EdgeRight[bin] = Math.Clamp(EdgeRight[bin], CarHalfWidth, 15f);
        EdgeLeft[bin] = Math.Clamp(EdgeLeft[bin], -15f, -CarHalfWidth);
    }
}
