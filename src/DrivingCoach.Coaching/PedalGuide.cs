using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Wo Gas und Bremse an einer Stelle der Strecke stehen sollten, jeweils 0..1.
/// </summary>
public readonly record struct PedalTarget(float Throttle, float Brake)
{
    /// <summary>True, wenn die Vorgabe an dieser Stelle das Bremspedal verlangt.</summary>
    public bool IsBraking => Brake > 0.05f;
}

/// <summary>
/// Liest ab, welche Pedalstellung an einer Stelle der Strecke die richtige wäre.
/// </summary>
/// <remarks>
/// <para>
/// Die Vorgabe kommt aus der Referenzrunde, nicht aus der gerechneten
/// Ideallinie. Das ist Absicht: Die Linie kennt nur ein Tempoprofil. Welche
/// Gasstellung zu einem Tempo gehört, hinge an Motorkennlinie, Luftwiderstand,
/// Getriebe und Abtrieb – alles Dinge, die die Schnittstelle von AMS2 nicht
/// hergibt. Eine daraus gerechnete Pedalvorgabe wäre geraten und in schnellen
/// Kurven grob falsch: Das Tempoprofil ist dort konstant, die nötige
/// Gasstellung aber alles andere als null.
/// </para>
/// <para>
/// Die Referenzrunde dagegen ist gemessen. Der Fahrer hat diese Pedalstellungen
/// an genau dieser Stelle schon einmal erreicht, die Vorgabe ist also
/// erreichbar – und sie wird mit jeder besseren Runde von selbst besser.
/// </para>
/// </remarks>
public static class PedalGuide
{
    /// <summary>
    /// Die Vorgabe an einer Distanz. <c>null</c>, solange es keine
    /// Referenzrunde gibt, gegen die sich etwas vorgeben ließe.
    /// </summary>
    public static PedalTarget? TargetAt(RecordedLap? reference, float lapDistance)
    {
        if (reference is null || reference.BinSize <= 0f || reference.Channels.Count == 0)
        {
            return null;
        }

        LapChannels channels = reference.Channels;

        return new PedalTarget(
            Math.Clamp(RecordedLap.Sample(channels.Throttle, reference.BinSize, lapDistance), 0f, 1f),
            Math.Clamp(RecordedLap.Sample(channels.Brake, reference.BinSize, lapDistance), 0f, 1f));
    }
}
