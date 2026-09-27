using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft die Pedalvorgabe – also das, was die gestrichelte Linie in der
/// Eingabe-Anzeige zeichnet.
/// </summary>
public sealed class PedalGuideTests
{
    /// <summary>
    /// Eine gefahrene Referenzrunde, geteilt von allen Tests dieser Klasse.
    /// Eine Runde zu fahren kostet ein paar Zehntelsekunden; sie ist unveränderlich,
    /// also reicht eine für alle.
    /// </summary>
    private static readonly RecordedLap Reference = VirtualDriver.DriveReferenceLap();

    private static readonly IReadOnlyList<Corner> Corners = new CornerDetector().Detect(Reference);

    [Fact]
    public void Ohne_Referenzrunde_gibt_es_keine_Vorgabe()
    {
        Assert.Null(PedalGuide.TargetAt(null, 500f));
    }

    [Fact]
    public void Die_Vorgabe_stammt_aus_der_Referenzrunde()
    {
        const float distance = 500f;

        PedalTarget target = TargetAt(distance);

        Assert.Equal(
            RecordedLap.Sample(Reference.Channels.Throttle, Reference.BinSize, distance),
            target.Throttle,
            precision: 3);
        Assert.Equal(
            RecordedLap.Sample(Reference.Channels.Brake, Reference.BinSize, distance),
            target.Brake,
            precision: 3);
    }

    /// <summary>
    /// Die Anzeige rechnet die Werte direkt in Balkenhöhen um. Ein Wert
    /// außerhalb von 0..1 zeichnete über den Rand des Streifens hinaus.
    /// </summary>
    [Fact]
    public void Die_Vorgabe_bleibt_ueberall_zwischen_null_und_eins()
    {
        for (float distance = 0f; distance < Reference.TrackLength; distance += 10f)
        {
            PedalTarget target = TargetAt(distance);

            Assert.InRange(target.Throttle, 0f, 1f);
            Assert.InRange(target.Brake, 0f, 1f);
        }
    }

    /// <summary>
    /// Der eigentliche Zweck: Vor einer Kurve muss die Vorgabe die Bremse
    /// verlangen. Täte sie das nirgends, bliebe die gestrichelte Bremslinie
    /// dauerhaft platt, und der Fahrer läse daraus, er brauche nie zu bremsen.
    /// </summary>
    [Fact]
    public void In_der_Bremszone_verlangt_die_Vorgabe_die_Bremse()
    {
        Corner braking = FirstBrakingCorner();

        float from = braking.BrakingStartBin * Reference.BinSize;
        float to = braking.StartBin * Reference.BinSize;

        bool anyBraking = false;
        for (float distance = from; distance <= to; distance += Reference.BinSize)
        {
            anyBraking |= TargetAt(distance).IsBraking;
        }

        Assert.True(anyBraking, $"Zwischen {from:0} m und {to:0} m steht in der Vorgabe kein Bremsdruck.");
    }

    /// <summary>
    /// Die Gegenprobe zur Bremszone: Auf der Geraden gehört kein Fuß auf die
    /// Bremse.
    /// </summary>
    [Fact]
    public void Auf_der_Geraden_verlangt_die_Vorgabe_keine_Bremse()
    {
        // 100 m vor dem Anbremspunkt – da ist die vorige Kurve durch und die
        // nächste noch weit weg.
        Corner braking = FirstBrakingCorner();
        float distance = MathF.Max(0f, (braking.BrakingStartBin * Reference.BinSize) - 100f);

        Assert.False(TargetAt(distance).IsBraking);
    }

    /// <summary>
    /// Irgendwo muss die Gasvorgabe auch nach unten zeigen – eine dauerhaft
    /// platte grüne Strichlinie wäre schlimmer als gar keine.
    /// </summary>
    /// <remarks>
    /// Geprüft über die ganze Runde statt an einer bestimmten Geraden, und das
    /// aus einem lehrreichen Grund: Der virtuelle Fahrer leitet seine
    /// Pedalstellung aus der Längsbeschleunigung ab, und die ist bei
    /// gleichbleibendem Tempo null – auf der Geraden steht in seinem Gaskanal
    /// deshalb 0 %. Genau diese Rechnung wäre herausgekommen, hätte man die
    /// Vorgabe aus dem Tempoprofil der Ideallinie gewonnen statt aus der
    /// gemessenen Referenzrunde. In AMS2 steht dort der echte Pedalweg.
    /// </remarks>
    [Fact]
    public void Die_Gasvorgabe_erreicht_irgendwo_volles_Gas()
    {
        float highest = 0f;
        for (float distance = 0f; distance < Reference.TrackLength; distance += Reference.BinSize)
        {
            highest = MathF.Max(highest, TargetAt(distance).Throttle);
        }

        Assert.True(highest > 0.5f, $"Die Gasvorgabe kommt nie über {highest:0.00} hinaus.");
    }

    /// <summary>
    /// Die Vorgabe wird bei jedem Frame abgefragt, auch an der Ziellinie. Dort
    /// darf sie nicht ins Leere greifen – <see cref="RecordedLap.Sample"/>
    /// rechnet zyklisch, und genau darauf verlässt sich der Aufrufer.
    /// </summary>
    [Fact]
    public void An_der_Ziellinie_bleibt_die_Vorgabe_stehen()
    {
        Assert.NotNull(PedalGuide.TargetAt(Reference, 0f));
        Assert.NotNull(PedalGuide.TargetAt(Reference, Reference.TrackLength - 0.5f));
        Assert.NotNull(PedalGuide.TargetAt(Reference, Reference.TrackLength + 5f));
        Assert.NotNull(PedalGuide.TargetAt(Reference, -5f));
    }

    private static PedalTarget TargetAt(float distance)
    {
        PedalTarget? target = PedalGuide.TargetAt(Reference, distance);
        Assert.True(target.HasValue, $"Bei {distance:0} m gibt es keine Vorgabe.");
        return target!.Value;
    }

    private static Corner FirstBrakingCorner()
    {
        Assert.NotEmpty(Corners);

        Corner? braking = Corners.FirstOrDefault(c => c.HasBrakingZone);
        Assert.True(braking is not null, "Keine der erkannten Kurven hat eine Bremsphase.");
        return braking!;
    }
}
