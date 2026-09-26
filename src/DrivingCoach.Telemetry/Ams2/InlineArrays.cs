using System.Runtime.CompilerServices;

namespace DrivingCoach.Telemetry.Ams2;

// Feste Arrays aus SharedMemory.h als InlineArray-Typen. Dadurch bleibt das
// Struct blittable (direkt per Zeiger lesbar, kein Marshalling), und das
// Speicherlayout entspricht exakt den C-Arrays.

[InlineArray(2)] public struct Float2 { private float _e0; }

[InlineArray(3)] public struct Float3 { private float _e0; }

[InlineArray(4)] public struct Float4 { private float _e0; }

[InlineArray(64)] public struct Float64 { private float _e0; }

/// <summary><c>float[64][3]</c> – flach als 192 Floats.</summary>
[InlineArray(192)] public struct Float192 { private float _e0; }

[InlineArray(4)] public struct UInt4 { private uint _e0; }

[InlineArray(64)] public struct UInt64Array { private uint _e0; }

/// <summary><c>char[64]</c> bzw. <c>bool[64]</c> – beides 64 Bytes.</summary>
[InlineArray(64)] public struct Byte64 { private byte _e0; }

/// <summary><c>char[40]</c> (TYRE_COMPOUND_NAME_LENGTH_MAX).</summary>
[InlineArray(40)] public struct Byte40 { private byte _e0; }

/// <summary><c>char[64][64]</c>.</summary>
[InlineArray(64)] public struct Byte64x64 { private Byte64 _e0; }

/// <summary><c>char[4][40]</c>.</summary>
[InlineArray(4)] public struct Byte4x40 { private Byte40 _e0; }

[InlineArray(64)] public struct ParticipantArray { private ParticipantInfo _e0; }
