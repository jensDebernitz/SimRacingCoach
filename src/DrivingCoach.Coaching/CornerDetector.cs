using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Findet die Kurven einer Strecke aus einer aufgezeichneten Runde.
/// </summary>
/// <remarks>
/// Grundlage ist die Krümmung der Fahrlinie, berechnet als Gierrate geteilt
/// durch Geschwindigkeit (rad/s ÷ m/s = rad/m). Der Lenkwinkel allein taugt
/// nicht: derselbe Einschlag bedeutet je nach Tempo und Lenkübersetzung eine
/// ganz andere Kurve, und Korrekturen am Lenkrad auf der Geraden würden als
/// Kurven durchgehen.
/// </remarks>
public sealed class CornerDetector
{
    /// <summary>Ab dieser Krümmung (≈ Radius 285 m) beginnt eine Kurve.</summary>
    private const float EnterCurvature = 0.0035f;

    /// <summary>Erst unterhalb dieser Krümmung (≈ Radius 500 m) endet sie wieder.</summary>
    private const float ExitCurvature = 0.0020f;

    /// <summary>Glättungsfenster in Metern – bügelt Lenkkorrekturen aus.</summary>
    private const float SmoothingWindowMetres = 20f;

    /// <summary>Kurven, die näher beieinander liegen, gelten als eine Kurvenkombination.</summary>
    private const float MergeGapMetres = 50f;

    /// <summary>Kürzere gekrümmte Abschnitte sind Knicke, keine Kurven.</summary>
    private const float MinCornerLengthMetres = 30f;

    /// <summary>So weit wird vor der Kurve nach dem Bremspunkt gesucht.</summary>
    private const float BrakeSearchMetres = 250f;

    /// <summary>Ab hier gilt das Bremspedal als betätigt.</summary>
    private const float BrakeThreshold = 0.08f;

    /// <summary>Ab hier gilt die Kurve als "wieder am Gas".</summary>
    private const float ThrottleThreshold = 0.85f;

    /// <summary>Unterhalb dieses Tempos ist die Krümmungsrechnung numerisch instabil.</summary>
    private const float MinSpeedForCurvature = 8f;

    /// <summary>Ermittelt alle Kurven einer Runde in Fahrtrichtung.</summary>
    public IReadOnlyList<Corner> Detect(RecordedLap lap)
    {
        LapChannels channels = lap.Channels;
        int count = channels.Count;
        if (count < 10)
        {
            return [];
        }

        float[] curvature = ComputeSmoothedCurvature(channels, lap.BinSize);
        List<(int Start, int End)> regions = FindRegions(curvature, lap.BinSize);
        regions = MergeAndFilter(regions, lap.BinSize, count);

        var corners = new List<Corner>(regions.Count);
        for (int i = 0; i < regions.Count; i++)
        {
            (int start, int end) = regions[i];
            corners.Add(BuildCorner(lap, i + 1, start, end, channels));
        }

        return corners;
    }

    /// <summary>Krümmung je Abschnitt, geglättet über ein gleitendes Fenster.</summary>
    private static float[] ComputeSmoothedCurvature(LapChannels channels, float binSize)
    {
        int count = channels.Count;
        var raw = new float[count];

        for (int i = 0; i < count; i++)
        {
            float speed = MathF.Max(channels.Speed[i], MinSpeedForCurvature);
            raw[i] = channels.YawRate[i] / speed;
        }

        int window = Math.Max(1, (int)(SmoothingWindowMetres / binSize));
        return MovingAverageCircular(raw, window);
    }

    /// <summary>
    /// Gleitender Mittelwert, der über Start/Ziel hinweg umläuft – eine Kurve
    /// darf schließlich direkt auf der Ziellinie liegen.
    /// </summary>
    private static float[] MovingAverageCircular(float[] values, int window)
    {
        int count = values.Length;
        var result = new float[count];
        int half = window / 2;

        for (int i = 0; i < count; i++)
        {
            float sum = 0f;
            for (int offset = -half; offset <= half; offset++)
            {
                int index = i + offset;
                index = ((index % count) + count) % count;
                sum += values[index];
            }

            result[i] = sum / (half * 2 + 1);
        }

        return result;
    }

    /// <summary>
    /// Findet zusammenhängende gekrümmte Bereiche mit Hysterese: einmal in der
    /// Kurve, wird erst bei deutlich geringerer Krümmung wieder ausgestiegen.
    /// Ohne das zerfällt eine lange Kurve in mehrere Fragmente.
    /// </summary>
    private static List<(int Start, int End)> FindRegions(float[] curvature, float binSize)
    {
        var regions = new List<(int Start, int End)>();
        int count = curvature.Length;

        bool inside = false;
        int start = 0;

        for (int i = 0; i < count; i++)
        {
            float magnitude = MathF.Abs(curvature[i]);

            if (!inside && magnitude >= EnterCurvature)
            {
                inside = true;
                start = i;
            }
            else if (inside && magnitude < ExitCurvature)
            {
                inside = false;
                regions.Add((start, i - 1));
            }
        }

        if (inside)
        {
            regions.Add((start, count - 1));
        }

        return regions;
    }

    private static List<(int Start, int End)> MergeAndFilter(
        List<(int Start, int End)> regions,
        float binSize,
        int binCount)
    {
        int mergeGap = (int)(MergeGapMetres / binSize);
        int minLength = (int)(MinCornerLengthMetres / binSize);

        var merged = new List<(int Start, int End)>();
        foreach ((int start, int end) in regions)
        {
            if (merged.Count > 0 && start - merged[^1].End <= mergeGap)
            {
                merged[^1] = (merged[^1].Start, end);
            }
            else
            {
                merged.Add((start, end));
            }
        }

        // Eine Kurve direkt auf Start/Ziel erscheint als zwei Bereiche am
        // Anfang und am Ende der Liste. Zusammenführen wäre nur mit
        // umlaufender Nummerierung sinnvoll und würde die Kurvennummern
        // verschieben – stattdessen bleiben beide Teile eigenständig.
        return merged.Where(r => r.End - r.Start >= minLength && r.End < binCount).ToList();
    }

    private static Corner BuildCorner(RecordedLap lap, int number, int start, int end, LapChannels channels)
    {
        int apex = start;
        float minSpeed = float.MaxValue;
        float yawSum = 0f;

        for (int i = start; i <= end; i++)
        {
            if (channels.Speed[i] < minSpeed)
            {
                minSpeed = channels.Speed[i];
                apex = i;
            }

            yawSum += channels.YawRate[i];
        }

        int brakingStart = FindBrakingStart(channels, lap.BinSize, start);
        int throttleBin = FindThrottlePoint(channels, apex, end);

        return new Corner(
            Number: number,
            StartBin: start,
            EndBin: end,
            ApexBin: apex,
            BrakingStartBin: brakingStart,
            ThrottleBin: throttleBin,
            Direction: yawSum >= 0f ? CornerDirection.Right : CornerDirection.Left,
            ApexSpeed: minSpeed,
            EntrySpeed: channels.Speed[Math.Max(brakingStart, 0)]);
    }

    /// <summary>
    /// Sucht rückwärts ab Kurveneingang nach der zusammenhängenden Bremsphase
    /// und liefert deren Beginn. Ohne Bremsphase ist das Ergebnis der
    /// Kurveneingang selbst.
    /// </summary>
    private static int FindBrakingStart(LapChannels channels, float binSize, int cornerStart)
    {
        int searchBins = (int)(BrakeSearchMetres / binSize);
        int limit = Math.Max(0, cornerStart - searchBins);

        int lastBraking = -1;
        for (int i = cornerStart; i >= limit; i--)
        {
            if (channels.Brake[i] > BrakeThreshold)
            {
                lastBraking = i;
                break;
            }
        }

        if (lastBraking < 0)
        {
            return cornerStart;
        }

        int begin = lastBraking;
        while (begin > limit && channels.Brake[begin - 1] > BrakeThreshold * 0.5f)
        {
            begin--;
        }

        return begin;
    }

    /// <summary>Erster Punkt ab dem Scheitel, an dem wieder voll beschleunigt wird.</summary>
    private static int FindThrottlePoint(LapChannels channels, int apex, int end)
    {
        int limit = Math.Min(channels.Count - 1, end + 20);
        for (int i = apex; i <= limit; i++)
        {
            if (channels.Throttle[i] >= ThrottleThreshold)
            {
                return i;
            }
        }

        return end;
    }
}
