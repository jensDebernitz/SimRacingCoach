using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Fasst dicht aufeinanderfolgende Kurven zu Kombinationen zusammen und
/// bewertet sie als Ganzes.
/// </summary>
/// <remarks>
/// <para>
/// In einer Kombination ist die einzelne Kurve die falsche Betrachtungseinheit.
/// Wer die erste Kurve einer Schikane perfekt trifft, steht für die zweite
/// falsch – und verliert auf der Geraden danach mehr, als er im Bogen davor
/// gewonnen hat. Ein Coach, der nur je Kurve meldet, würde den Fahrer genau in
/// diesen Fehler hineinberaten.
/// </para>
/// <para>
/// Die Länge der folgenden Geraden ist deshalb Teil des Modells: Sie bestimmt,
/// wie viel der Ausgang der letzten Kurve überhaupt wert ist.
/// </para>
/// </remarks>
public static class SequenceAnalyzer
{
    /// <summary>Bis zu diesem Abstand gehören zwei Kurven zusammen.</summary>
    /// <remarks>
    /// Deutlich großzügiger als das Zusammenfassen im <see cref="CornerDetector"/>
    /// (50 m): Dort geht es darum, ob zwei gekrümmte Abschnitte <em>eine</em>
    /// Kurve sind. Hier geht es darum, ob die eine die andere beeinflusst – und
    /// das tut sie auch dann noch, wenn 100 m dazwischen liegen.
    /// </remarks>
    private const float LinkGapMetres = 130f;

    /// <summary>Unterhalb dieses Verlusts lohnt kein Hinweis zur Kombination.</summary>
    private const float MinRelevantTimeLoss = 0.06f;

    /// <summary>Ab diesem Anteil am Gesamtverlust gilt eine Kurve als der Übeltäter.</summary>
    private const float DominantShare = 0.55f;

    /// <summary>Fasst die erkannten Kurven zu Kombinationen zusammen.</summary>
    /// <param name="corners">Die Kurven der Referenzrunde in Fahrtrichtung.</param>
    /// <param name="binSize">Abschnittsbreite der Referenzrunde in Metern.</param>
    /// <param name="trackLength">Streckenlänge in Metern.</param>
    public static IReadOnlyList<CornerSequence> Detect(
        IReadOnlyList<Corner> corners,
        float binSize,
        float trackLength)
    {
        if (corners.Count == 0 || binSize <= 0f)
        {
            return [];
        }

        var groups = new List<List<Corner>> { new() { corners[0] } };

        for (int i = 1; i < corners.Count; i++)
        {
            float gap = (corners[i].StartBin - corners[i - 1].EndBin) * binSize;

            if (gap <= LinkGapMetres)
            {
                groups[^1].Add(corners[i]);
            }
            else
            {
                groups.Add([corners[i]]);
            }
        }

        var sequences = new List<CornerSequence>(groups.Count);

        for (int i = 0; i < groups.Count; i++)
        {
            List<Corner> group = groups[i];
            float end = group[^1].EndBin * binSize;
            float nextBraking = EntryOf(groups[(i + 1) % groups.Count][0], binSize);

            // Die letzte Kombination misst über Start/Ziel hinweg bis zur
            // ersten – auf einer Rundstrecke ist das dieselbe Gerade.
            float straight = i + 1 < groups.Count
                ? nextBraking - end
                : trackLength - end + nextBraking;

            sequences.Add(new CornerSequence(i + 1, group, MathF.Max(0f, straight)));
        }

        return sequences;
    }

    /// <summary>Bewertet jede Kombination anhand der Verteilung des Zeitverlusts.</summary>
    /// <param name="sequences">Die erkannten Kombinationen.</param>
    /// <param name="advice">Die Kurvenauswertung derselben Runde.</param>
    public static IReadOnlyList<SequenceAdvice> Analyze(
        IReadOnlyList<CornerSequence> sequences,
        IReadOnlyList<CornerAdvice> advice)
    {
        Dictionary<int, CornerAdvice> byNumber = advice
            .GroupBy(a => a.Corner.Number)
            .ToDictionary(g => g.Key, g => g.First());

        var results = new List<SequenceAdvice>();

        foreach (CornerSequence sequence in sequences.Where(s => s.IsCombination))
        {
            results.Add(Evaluate(sequence, byNumber));
        }

        return results;
    }

    private static SequenceAdvice Evaluate(CornerSequence sequence, Dictionary<int, CornerAdvice> byNumber)
    {
        float total = 0f;
        float first = 0f;
        float last = 0f;

        for (int i = 0; i < sequence.Corners.Count; i++)
        {
            if (!byNumber.TryGetValue(sequence.Corners[i].Number, out CornerAdvice? item))
            {
                continue;
            }

            total += item.TimeLost;

            if (i == 0)
            {
                first = item.TimeLost;
            }

            if (i == sequence.Corners.Count - 1)
            {
                last = item.TimeLost;
            }
        }

        if (total < MinRelevantTimeLoss)
        {
            return new SequenceAdvice(sequence, SequenceIssue.None, total, string.Empty, string.Empty);
        }

        string shape = $"{sequence.Name} ({sequence.Shape})";

        if (last > 0f && last >= total * DominantShare)
        {
            string stakes = sequence.LeadsOntoStraight
                ? $"und danach kommen {sequence.FollowingStraightMetres:0} m Gerade"
                : "und der Ausgang zieht sich in den nächsten Abschnitt";

            return new SequenceAdvice(
                sequence,
                SequenceIssue.ExitCompromised,
                total,
                $"{shape}: der Verlust steckt fast ganz in {sequence.Last.Name}, {stakes}. Opfere den Eingang – langsamer und weiter außen in {sequence.First.Name}, damit die letzte Kurve stimmt · {total:0.00} s",
                $"{sequence.SpeechName}: opfere den Eingang für den Ausgang.");
        }

        if (first > 0f && first >= total * DominantShare)
        {
            return new SequenceAdvice(
                sequence,
                SequenceIssue.EntryOverdriven,
                total,
                $"{shape}: du überfährst {sequence.First.Name} und rettest dich durch den Rest – früher bremsen und die erste Kurve sauber anlegen, dann fällt der Rest von selbst · {total:0.00} s",
                $"{sequence.SpeechName}: früher bremsen, die erste Kurve anlegen.");
        }

        return new SequenceAdvice(
            sequence,
            SequenceIssue.Throughout,
            total,
            $"{shape}: der Verlust verteilt sich über die ganze Kombination – fahr sie als einen Bogen, nicht als einzelne Kurven · {total:0.00} s",
            $"{sequence.SpeechName}: als einen Bogen fahren.");
    }

    /// <summary>Wo die Kurve beginnt, Bremszone eingerechnet.</summary>
    private static float EntryOf(Corner corner, float binSize) =>
        Math.Min(corner.BrakingStartBin, corner.StartBin) * binSize;
}
