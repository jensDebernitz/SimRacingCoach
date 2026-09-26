using System.Runtime.CompilerServices;
using System.Text;

namespace DrivingCoach.Telemetry.Ams2;

/// <summary>
/// Konstanten aus <c>SharedMemory.h</c> und eine Selbstprüfung des Speicherlayouts.
/// </summary>
public static class Ams2Layout
{
    /// <summary>Name der Memory-Mapped-File, die AMS2 bereitstellt.</summary>
    public const string SharedMemoryName = "$pcars2$";

    /// <summary>SHARED_MEMORY_VERSION, gegen die diese Portierung geschrieben ist.</summary>
    public const uint ExpectedVersion = 14;

    public const int StringLengthMax = 64;
    public const int StoredParticipantsMax = 64;
    public const int TyreCompoundNameLengthMax = 40;
    public const int TyreMax = 4;

    public const int TyreFrontLeft = 0;
    public const int TyreFrontRight = 1;
    public const int TyreRearLeft = 2;
    public const int TyreRearRight = 3;

    public const int VecX = 0;
    public const int VecY = 1;
    public const int VecZ = 2;

    /// <summary>
    /// Erwartete Größe von <see cref="ParticipantInfo"/>: 1 Byte bool + 64 Byte Name
    /// + 3 Padding + 12 Byte Position + 5 × 4 Byte = 100.
    /// </summary>
    public const int ExpectedParticipantInfoSize = 100;

    /// <summary>
    /// Erwartete Gesamtgröße des Blocks bei natürlicher 4-Byte-Ausrichtung.
    /// Von Hand aus dem Header ausgezählt; <see cref="Validate"/> prüft, ob der
    /// C#-Compiler auf denselben Wert kommt.
    /// </summary>
    public const int ExpectedSharedMemorySize = 20700;

    /// <summary>
    /// Prüft, ob das portierte Struct das erwartete Layout hat. Weicht etwas ab,
    /// ist die Portierung fehlerhaft und jede gelesene Telemetrie wäre Müll –
    /// dann lieber laut scheitern als still falsche Zahlen anzeigen.
    /// </summary>
    /// <param name="error">Beschreibung der Abweichung, sonst <c>null</c>.</param>
    public static bool Validate(out string? error)
    {
        int participant = Unsafe.SizeOf<ParticipantInfo>();
        if (participant != ExpectedParticipantInfoSize)
        {
            error = $"ParticipantInfo ist {participant} Byte groß, erwartet {ExpectedParticipantInfoSize}.";
            return false;
        }

        int total = Unsafe.SizeOf<Ams2SharedMemory>();
        if (total != ExpectedSharedMemorySize)
        {
            error = $"Ams2SharedMemory ist {total} Byte groß, erwartet {ExpectedSharedMemorySize}.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Wandelt ein <c>char[64]</c>-Feld aus dem Shared Memory in einen String.
    /// AMS2 füllt ungenutzte Bytes mit 0; alles ab dem ersten Nullbyte wird verworfen.
    /// </summary>
    public static string ToText(this Byte64 raw)
    {
        ReadOnlySpan<byte> span = raw;
        return Decode(span);
    }

    /// <inheritdoc cref="ToText(Byte64)"/>
    public static string ToText(this Byte40 raw)
    {
        ReadOnlySpan<byte> span = raw;
        return Decode(span);
    }

    private static string Decode(ReadOnlySpan<byte> span)
    {
        int end = span.IndexOf((byte)0);
        if (end >= 0)
        {
            span = span[..end];
        }

        return span.IsEmpty ? string.Empty : Encoding.UTF8.GetString(span).Trim();
    }
}
