using System.Runtime.CompilerServices;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft die Portierung von <c>SharedMemory.h</c>. Das Layout ist die Grundlage
/// für alles Weitere – stimmt es nicht, liefert der Reader still falsche Werte.
/// </summary>
public class Ams2LayoutTests
{
    [Fact]
    public void ParticipantInfo_HatDieLayoutGroesseAusDemHeader()
    {
        Assert.Equal(Ams2Layout.ExpectedParticipantInfoSize, Unsafe.SizeOf<ParticipantInfo>());
    }

    [Fact]
    public void SharedMemory_HatDieLayoutGroesseAusDemHeader()
    {
        Assert.Equal(Ams2Layout.ExpectedSharedMemorySize, Unsafe.SizeOf<Ams2SharedMemory>());
    }

    [Fact]
    public void Validate_MeldetKeinenFehler()
    {
        Assert.True(Ams2Layout.Validate(out string? error), error);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(3, 12)]
    [InlineData(4, 16)]
    [InlineData(64, 256)]
    public void InlineFloatArrays_HabenDieErwarteteGroesse(int elements, int expectedBytes)
    {
        int actual = elements switch
        {
            2 => Unsafe.SizeOf<Float2>(),
            3 => Unsafe.SizeOf<Float3>(),
            4 => Unsafe.SizeOf<Float4>(),
            64 => Unsafe.SizeOf<Float64>(),
            _ => throw new ArgumentOutOfRangeException(nameof(elements)),
        };

        Assert.Equal(expectedBytes, actual);
    }

    [Fact]
    public void ZweidimensionaleArrays_HabenDieErwarteteGroesse()
    {
        Assert.Equal(192 * sizeof(float), Unsafe.SizeOf<Float192>());   // float[64][3]
        Assert.Equal(64 * 64, Unsafe.SizeOf<Byte64x64>());              // char[64][64]
        Assert.Equal(4 * 40, Unsafe.SizeOf<Byte4x40>());                // char[4][40]
        Assert.Equal(64 * 100, Unsafe.SizeOf<ParticipantArray>());      // ParticipantInfo[64]
    }

    [Fact]
    public void ToText_SchneidetAmNullByteAb()
    {
        Byte64 raw = default;
        ReadOnlySpan<byte> source = "Interlagos\0Reste"u8;
        source.CopyTo(raw);

        Assert.Equal("Interlagos", raw.ToText());
    }

    [Fact]
    public void ToText_LiefertLeerstringFuerUnbefuelltesFeld()
    {
        Byte64 raw = default;
        Assert.Equal(string.Empty, raw.ToText());
    }
}
