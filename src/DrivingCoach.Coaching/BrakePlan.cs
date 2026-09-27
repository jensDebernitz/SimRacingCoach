using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Wie fest für eine Kurve getreten werden muss – und wo die Bremse wieder
/// aufgemacht wird.
/// </summary>
/// <param name="Peak">Spitzendruck am Anfang der Bremsphase, 0..1.</param>
/// <param name="ReleaseTo">
/// Druck, auf den gelöst wird, 0..1. <see cref="float.NaN"/>, wenn es nichts zu
/// lösen gibt – etwa bei einem kurzen Antippen vor einer schnellen Kurve.
/// </param>
/// <param name="ReleaseAtMetres">
/// Restweg bis zum Ende der Bremsphase, an dem gelöst wird.
/// <see cref="float.NaN"/> zusammen mit <paramref name="ReleaseTo"/>.
/// </param>
/// <param name="EndMetres">
/// Weg vom Bremspunkt bis dahin, wo die Bremse wieder offen ist.
/// </param>
public readonly record struct BrakePlan(
    float Peak,
    float ReleaseTo,
    float ReleaseAtMetres,
    float EndMetres)
{
    /// <summary>Die Prozentangaben laufen auf diesem Raster.</summary>
    /// <remarks>
    /// Feiner hätte keinen Wert: Niemand trifft 83 % Bremsdruck, und eine Zahl,
    /// die bei jedem Frame um einen Punkt springt, ist im Augenwinkel nicht zu
    /// lesen.
    /// </remarks>
    private const int Grid = 5;

    /// <summary>Ab diesem Anteil gilt der Spitzendruck als volle Bremsung.</summary>
    private const int FullPercent = 95;

    /// <summary>Ab hier zählt die Bremse als getreten.</summary>
    private const float OnThreshold = 0.08f;

    /// <summary>
    /// Unter diesem Anteil des Spitzendrucks ist die Bremsphase vorbei.
    /// </summary>
    private const float OffFraction = 0.25f;

    /// <summary>
    /// So weit hinter dem Bremspunkt wird der Anfang der Bremsphase noch
    /// gesucht.
    /// </summary>
    /// <remarks>
    /// Der Bremspunkt der Ideallinie liegt in aller Regel vor dem, an dem in der
    /// Referenzrunde tatsächlich getreten wurde – genau das ist ja der Tipp.
    /// Ohne diesen Vorlauf wäre der Bremsdruck am Bremspunkt null und es gäbe
    /// nie eine Vorgabe.
    /// </remarks>
    private const float LeadInMetres = 120f;

    /// <summary>Länger als das ist keine Bremsphase, sondern ein Messfehler.</summary>
    private const float MaxZoneMetres = 320f;

    /// <summary>Kürzere Bremsphasen lohnen keine Vorgabe.</summary>
    private const float MinZoneMetres = 20f;

    /// <summary>Darunter war es ein Antippen, kein Bremsen.</summary>
    private const float MinPeak = 0.15f;

    /// <summary>
    /// An dieser Stelle der Bremsphase wird der Lösepunkt abgelesen, gerechnet
    /// vom Spitzendruck bis zum Ende.
    /// </summary>
    /// <remarks>
    /// Etwas hinter der Mitte, weil das Lösen beim Anbremsen erst nach der
    /// Spitze beginnt und zum Scheitel hin flacher ausläuft. Ein einziger
    /// Kontrollpunkt genügt: Zwei Zahlen kann sich ein Fahrer merken, eine
    /// gesprochene Kurve nicht.
    /// </remarks>
    private const float ReleaseFraction = 0.55f;

    /// <summary>So viel muss der Druck fallen, damit sich das Ansagen lohnt.</summary>
    private const float MinDrop = 0.15f;

    /// <summary>Und so lange muss danach noch Bremsphase übrig sein.</summary>
    private const float MinReleaseMetres = 20f;

    /// <summary>True, wenn die Vorgabe einen Lösepunkt nennt.</summary>
    public bool HasRelease => !float.IsNaN(ReleaseAtMetres);

    /// <summary>Spitzendruck in Prozent.</summary>
    public int PeakPercent => Percent(Peak);

    /// <summary>Druck nach dem Lösen in Prozent.</summary>
    public int ReleasePercent => Percent(ReleaseTo);

    /// <summary>True, wenn die Kurve volle Bremsung verlangt.</summary>
    public bool IsFull => PeakPercent >= FullPercent;

    /// <summary>Der Spitzendruck für die Anzeige.</summary>
    public string ForceText => $"{PeakPercent} %";

    /// <summary>
    /// Der Bremsruf. "Voll" statt "100 Prozent", weil im Moment des Tritts jede
    /// Silbe zählt.
    /// </summary>
    public string CallText => IsFull ? "Bremsen, voll" : $"Bremsen, {PeakPercent} Prozent";

    /// <summary>Der Ruf am Lösepunkt.</summary>
    public string ReleaseCallText => $"Lösen auf {ReleasePercent}";

    /// <summary>Rundet einen Pedalwert auf das Anzeigeraster.</summary>
    public static int Percent(float force) =>
        float.IsNaN(force) ? 0 : (int)MathF.Round(Math.Clamp(force, 0f, 1f) * 100f / Grid) * Grid;

    /// <summary>
    /// Liest die Bremskraft-Vorgabe aus der Referenzrunde ab.
    /// <c>null</c>, wenn an diesem Bremspunkt nichts zu beschreiben ist.
    /// </summary>
    /// <remarks>
    /// Die Zahlen sind gemessen, nicht gerechnet – aus demselben Grund wie bei
    /// <see cref="PedalGuide"/>: Welcher Bremsdruck an einer Stelle möglich ist,
    /// hängt an Reifen, Abtrieb, Bremsbalance und Beladung, und davon gibt die
    /// Schnittstelle von AMS2 nichts her. Was der Fahrer dort schon einmal
    /// getreten hat, ist dagegen belegbar erreichbar.
    /// </remarks>
    /// <param name="reference">Die Referenzrunde.</param>
    /// <param name="brakePoint">Der Bremspunkt in Metern ab Start/Ziel.</param>
    public static BrakePlan? For(RecordedLap? reference, float brakePoint)
    {
        if (reference is null || reference.BinSize <= 0f || reference.Channels.Count == 0)
        {
            return null;
        }

        float step = reference.BinSize;
        float[] brake = reference.Channels.Brake;

        // Alle Wege sind ab hier relativ zum Bremspunkt gerechnet.
        float start = float.NaN;
        float end = float.NaN;
        float peak = 0f;
        float peakAt = 0f;

        for (float d = 0f; d <= MaxZoneMetres; d += step)
        {
            float force = ForceAt(brake, step, brakePoint + d);

            if (float.IsNaN(start))
            {
                if (force >= OnThreshold)
                {
                    start = d;
                    peak = force;
                    peakAt = d;
                }
                else if (d >= LeadInMetres)
                {
                    // So weit hinter dem Bremspunkt und immer noch kein Druck:
                    // Zu diesem Punkt gehört keine Bremsphase.
                    return null;
                }

                continue;
            }

            if (force > peak)
            {
                peak = force;
                peakAt = d;
            }

            // Erst nach der Spitze darf das Ende gefunden werden – sonst endete
            // die Phase schon im Aufbau des Drucks.
            if (d > peakAt && force < MathF.Max(OnThreshold, peak * OffFraction))
            {
                end = d;
                break;
            }
        }

        if (float.IsNaN(start))
        {
            return null;
        }

        // Ohne erkanntes Ende bricht die Suche am Anschlag ab. Das ist selten
        // und meist eine lange Bergabbremsung; die Vorgabe bleibt trotzdem
        // brauchbar, nur der Restweg stimmt dann nicht auf den Meter.
        if (float.IsNaN(end))
        {
            end = MaxZoneMetres;
        }

        if (peak < MinPeak || end - start < MinZoneMetres)
        {
            return null;
        }

        float checkpoint = peakAt + ((end - peakAt) * ReleaseFraction);

        // Über drei Abtastungen gemittelt: Ein einzelner Wert aus der
        // Referenzrunde kann ein Zucken am Pedal sein.
        float releaseTo =
            (ForceAt(brake, step, brakePoint + checkpoint - step)
             + ForceAt(brake, step, brakePoint + checkpoint)
             + ForceAt(brake, step, brakePoint + checkpoint + step)) / 3f;

        float releaseAt = end - checkpoint;
        bool hasRelease = releaseAt >= MinReleaseMetres && peak - releaseTo >= MinDrop;

        return new BrakePlan(
            peak,
            hasRelease ? releaseTo : float.NaN,
            hasRelease ? releaseAt : float.NaN,
            end);
    }

    private static float ForceAt(float[] brake, float step, float distance) =>
        Math.Clamp(RecordedLap.Sample(brake, step, distance), 0f, 1f);
}

/// <summary>
/// Eine laufende Bremsphase – das Auto steckt gerade darin.
/// </summary>
/// <param name="Corner">Die Kurve, für die gebremst wird.</param>
/// <param name="Plan">Wie fest, und wo gelöst wird.</param>
/// <param name="MetresLeft">Restweg bis zum Ende der Bremsphase.</param>
public readonly record struct ActiveBraking(Corner Corner, BrakePlan Plan, float MetresLeft);

/// <summary>
/// Soll und Ist am Bremspedal, während gebremst wird.
/// </summary>
/// <param name="CornerName">Die Kurve, für die gebremst wird.</param>
/// <param name="Target">Sollwert an dieser Stelle, 0..1.</param>
/// <param name="Actual">Was der Fahrer gerade tritt, 0..1.</param>
/// <param name="ReleaseInMetres">
/// Weg bis zum Lösepunkt. <see cref="float.NaN"/>, wenn keiner mehr kommt.
/// </param>
/// <param name="ReleaseTo">Druck nach dem Lösen, 0..1.</param>
public readonly record struct BrakeForceCue(
    string CornerName,
    float Target,
    float Actual,
    float ReleaseInMetres,
    float ReleaseTo)
{
    /// <summary>Ab diesem Abstand zwischen Soll und Ist färbt sich die Anzeige um.</summary>
    private const float OffBand = 0.15f;

    /// <summary>Sollwert in Prozent.</summary>
    public int TargetPercent => BrakePlan.Percent(Target);

    /// <summary>Tatsächlicher Druck in Prozent.</summary>
    public int ActualPercent => BrakePlan.Percent(Actual);

    /// <summary>Druck nach dem Lösen, in Prozent.</summary>
    public int ReleasePercent => BrakePlan.Percent(ReleaseTo);

    /// <summary>True, solange der Lösepunkt noch bevorsteht.</summary>
    public bool HasRelease => !float.IsNaN(ReleaseInMetres);

    /// <summary>True, wenn Soll und Ist deutlich auseinanderliegen.</summary>
    public bool IsOff => MathF.Abs(Actual - Target) > OffBand;

    /// <summary>Die Füllung des Balkens zeigt den tatsächlichen Druck.</summary>
    public double Progress => Math.Clamp(Actual, 0f, 1f);

    /// <summary>Die Zeile über dem Balken.</summary>
    public string Text => HasRelease
        ? $"{CornerName}  ·  Bremse {ActualPercent} %  ·  Soll {TargetPercent} %  ·  in {ReleaseInMetres:0} m auf {ReleasePercent} %"
        : $"{CornerName}  ·  Bremse {ActualPercent} %  ·  Soll {TargetPercent} %";
}
