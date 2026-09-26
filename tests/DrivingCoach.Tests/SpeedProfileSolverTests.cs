using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

/// <summary>
/// Der Solver ist die Physik hinter Simulator und Tests. Stimmt er nicht,
/// prüfen alle anderen Tests unfahrbare Daten.
/// </summary>
public class SpeedProfileSolverTests
{
    private const float Step = SimTrack.StepMetres;
    private const float Accel = SimTrack.MaxAcceleration;
    private const float Decel = SimTrack.MaxDeceleration;

    private static float[] DefaultProfile()
    {
        var scales = new float[SimTrack.Default.Corners.Count];
        Array.Fill(scales, 1f);
        return SpeedProfileSolver.Solve(SimTrack.Default.BuildSpeedLimits(scales), Step, Accel, Decel);
    }

    [Fact]
    public void Ueberschreitet_nirgends_das_Tempolimit()
    {
        var scales = new float[SimTrack.Default.Corners.Count];
        Array.Fill(scales, 1f);

        float[] limits = SimTrack.Default.BuildSpeedLimits(scales);
        float[] profile = SpeedProfileSolver.Solve(limits, Step, Accel, Decel);

        for (int i = 0; i < profile.Length; i++)
        {
            Assert.True(profile[i] <= limits[i] + 0.001f, $"Abschnitt {i}: {profile[i]:0.00} > {limits[i]:0.00} m/s.");
        }
    }

    [Fact]
    public void Haelt_Beschleunigung_und_Verzoegerung_ein()
    {
        float[] profile = DefaultProfile();
        int count = profile.Length;

        for (int i = 0; i < count; i++)
        {
            int next = SpeedProfileSolver.Wrap(i + 1, count);

            // v²(next) = v²(i) ± 2·a·s – mit kleiner Toleranz für float-Rundung.
            float gained = profile[next] * profile[next] - profile[i] * profile[i];

            Assert.True(gained <= 2f * Accel * Step + 0.5f, $"Abschnitt {i}: zu starke Beschleunigung.");
            Assert.True(-gained <= 2f * Decel * Step + 0.5f, $"Abschnitt {i}: zu starke Verzögerung.");
        }
    }

    [Fact]
    public void Passt_auch_ueber_Start_und_Ziel_zusammen()
    {
        float[] profile = DefaultProfile();

        // Die letzte Kurve der Teststrecke liegt 380 m vor der Linie. Ein
        // Durchlauf würde die Bremszone dorthin legen, aber die Anbremszone der
        // ersten Kurve hinter der Linie nicht mehr berücksichtigen.
        float first = profile[0];
        float last = profile[^1];
        float difference = MathF.Abs(first * first - last * last);

        Assert.True(
            difference <= 2f * MathF.Max(Accel, Decel) * Step + 0.5f,
            $"Sprung über Start/Ziel: {last:0.0} m/s → {first:0.0} m/s.");
    }

    [Fact]
    public void Bremst_rechtzeitig_vor_jeder_Kurve()
    {
        float[] profile = DefaultProfile();
        SimTrack track = SimTrack.Default;

        foreach (SimCorner corner in track.Corners)
        {
            int entry = (int)(corner.EntryDistance / Step);
            Assert.True(
                profile[track.Wrap(entry)] <= corner.ApexSpeed + 0.5f,
                $"Kurve bei {corner.EntryDistance:0} m wird mit {profile[track.Wrap(entry)]:0.0} m/s angefahren.");
        }
    }

    [Fact]
    public void Langsamere_Kurvenfaktoren_ergeben_ein_langsameres_Profil()
    {
        var fastScales = new float[SimTrack.Default.Corners.Count];
        Array.Fill(fastScales, 1f);

        var slowScales = new float[SimTrack.Default.Corners.Count];
        Array.Fill(slowScales, 0.8f);

        float[] fast = SpeedProfileSolver.Solve(SimTrack.Default.BuildSpeedLimits(fastScales), Step, Accel, Decel);
        float[] slow = SpeedProfileSolver.Solve(SimTrack.Default.BuildSpeedLimits(slowScales), Step, Accel, Decel);

        for (int i = 0; i < fast.Length; i++)
        {
            Assert.True(slow[i] <= fast[i] + 0.001f);
        }

        Assert.True(slow.Sum() < fast.Sum());
    }

    [Theory]
    [InlineData(-1, 10, 9)]
    [InlineData(0, 10, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(-11, 10, 9)]
    [InlineData(23, 10, 3)]
    public void Wrap_bleibt_im_gueltigen_Bereich(int index, int count, int expected)
    {
        Assert.Equal(expected, SpeedProfileSolver.Wrap(index, count));
    }
}
