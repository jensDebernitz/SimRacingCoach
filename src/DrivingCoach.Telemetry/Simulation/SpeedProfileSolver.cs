namespace DrivingCoach.Telemetry.Simulation;

/// <summary>
/// Macht aus reinen Tempolimits ein fahrbares Geschwindigkeitsprofil.
/// </summary>
/// <remarks>
/// Zwei Durchläufe über die Runde: vorwärts begrenzt die Beschleunigung, wie
/// schnell das Auto wieder auf Tempo kommt; rückwärts begrenzt die
/// Verzögerung, wie spät es überhaupt noch auf Kurventempo herunterkommt. Aus
/// dem Rückwärtslauf entstehen die Bremszonen an genau den richtigen Stellen –
/// dieselbe Idee wie bei einem einfachen Rundenzeit-Simulator.
/// </remarks>
public static class SpeedProfileSolver
{
    /// <summary>
    /// Löst das Profil für eine geschlossene Runde.
    /// </summary>
    /// <param name="limits">Tempolimit je Abschnitt in m/s.</param>
    /// <param name="stepMetres">Abschnittsbreite in Metern.</param>
    /// <param name="maxAcceleration">Längsbeschleunigung in m/s².</param>
    /// <param name="maxDeceleration">Verzögerung in m/s².</param>
    /// <param name="passes">
    /// Anzahl Durchläufe. Zwei sind nötig, damit das Profil auch über
    /// Start/Ziel hinweg zusammenpasst – der erste Rückwärtslauf kennt die
    /// Bremszone am Rundenende noch nicht.
    /// </param>
    public static float[] Solve(
        float[] limits,
        float stepMetres,
        float maxAcceleration,
        float maxDeceleration,
        int passes = 2)
    {
        var steps = new float[limits.Length];
        Array.Fill(steps, stepMetres);
        return Solve(limits, steps, maxAcceleration, maxDeceleration, passes);
    }

    /// <summary>
    /// Wie <see cref="Solve(float[], float, float, float, int)"/>, aber mit
    /// einer eigenen Länge je Abschnitt.
    /// </summary>
    /// <param name="stepMetres">
    /// Abstand von Abschnitt <c>i</c> zu <c>i+1</c> in Metern.
    /// </param>
    /// <remarks>
    /// Für die Ideallinie sind die Abschnitte nicht gleich lang: Sie werden
    /// quer zur Mittellinie abgegriffen, und wer außen herumfährt, legt
    /// zwischen zwei Abschnitten mehr Weg zurück als einer, der innen
    /// abkürzt. Mit einer festen Schrittweite käme eine Rundenzeit heraus, die
    /// die Abkürzung nicht belohnt.
    /// </remarks>
    public static float[] Solve(
        float[] limits,
        float[] stepMetres,
        float maxAcceleration,
        float maxDeceleration,
        int passes = 2)
    {
        int count = limits.Length;
        var speeds = (float[])limits.Clone();

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                int previous = Wrap(i - 1, count);
                speeds[i] = MathF.Min(
                    speeds[i],
                    MathF.Sqrt((speeds[previous] * speeds[previous]) + (2f * maxAcceleration * stepMetres[previous])));
            }

            for (int i = count - 1; i >= 0; i--)
            {
                int next = Wrap(i + 1, count);
                speeds[i] = MathF.Min(
                    speeds[i],
                    MathF.Sqrt((speeds[next] * speeds[next]) + (2f * maxDeceleration * stepMetres[i])));
            }
        }

        return speeds;
    }

    /// <summary>Index zyklisch in den gültigen Bereich bringen.</summary>
    public static int Wrap(int index, int count)
    {
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }
}
