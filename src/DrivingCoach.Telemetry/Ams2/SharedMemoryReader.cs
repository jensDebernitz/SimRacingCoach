using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace DrivingCoach.Telemetry.Ams2;

/// <summary>Verbindungszustand zum Shared Memory von AMS2.</summary>
public enum ConnectionState
{
    /// <summary>Noch kein Versuch unternommen.</summary>
    Idle,

    /// <summary>AMS2 läuft nicht oder Shared Memory ist im Spiel nicht aktiviert.</summary>
    GameNotRunning,

    /// <summary>Verbunden, aber das Spiel meldet eine andere Struct-Version als erwartet.</summary>
    VersionMismatch,

    /// <summary>Verbunden und Daten plausibel.</summary>
    Connected,
}

/// <summary>
/// Liest den <c>$pcars2$</c>-Block von AMS2 direkt per Zeiger.
/// </summary>
/// <remarks>
/// AMS2 schreibt den Block einmal pro Grafik-Frame und markiert das über
/// <c>mSequenceNumber</c>: ungerade während des Schreibens, gerade wenn er ruht.
/// Ohne diese Prüfung kann ein Lesevorgang einen halb beschriebenen Block
/// erwischen ("torn read") und z. B. Distanz aus Frame N mit Zeit aus Frame N+1
/// mischen – was genau die Werte verfälscht, auf denen das Delta beruht.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe class SharedMemoryReader : IDisposable
{
    /// <summary>
    /// Die offiziellen Beispiele öffnen den Block ohne Namespace-Präfix. Manche
    /// Konstellationen (Spiel als Administrator, andere Session) machen das
    /// explizite <c>Local\</c> nötig – deshalb beide Varianten versuchen.
    /// </summary>
    private static readonly string[] CandidateNames =
    [
        Ams2Layout.SharedMemoryName,
        $@"Local\{Ams2Layout.SharedMemoryName}",
    ];

    /// <summary>Wie oft ein zerrissener Lesevorgang wiederholt wird, bevor der Frame verworfen wird.</summary>
    private const int MaxReadAttempts = 8;

    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private byte* _pointer;
    private bool _pointerAcquired;

    /// <summary>Aktueller Verbindungszustand.</summary>
    public ConnectionState State { get; private set; } = ConnectionState.Idle;

    /// <summary>Letzte Fehlermeldung, falls die Verbindung fehlschlug.</summary>
    public string? LastError { get; private set; }

    /// <summary>Vom Spiel gemeldete Struct-Version (0, solange nicht verbunden).</summary>
    public uint ReportedVersion { get; private set; }

    /// <summary>Anzahl verworfener Frames wegen zerrissener Lesevorgänge.</summary>
    public long TornReads { get; private set; }

    public bool IsConnected => _pointerAcquired;

    /// <summary>
    /// Versucht, den Shared-Memory-Block zu öffnen. Schlägt fehl, solange AMS2
    /// nicht läuft oder Shared Memory im Spiel nicht auf "Project CARS 2" steht.
    /// </summary>
    public bool TryConnect()
    {
        if (_pointerAcquired)
        {
            return true;
        }

        if (!Ams2Layout.Validate(out string? layoutError))
        {
            State = ConnectionState.VersionMismatch;
            LastError = $"Interner Layout-Fehler: {layoutError}";
            return false;
        }

        foreach (string name in CandidateNames)
        {
            try
            {
                _file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                _view = _file.CreateViewAccessor(0, Ams2Layout.ExpectedSharedMemorySize, MemoryMappedFileAccess.Read);
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
                _pointerAcquired = true;

                ReportedVersion = ((Ams2SharedMemory*)_pointer)->mVersion;
                if (ReportedVersion != Ams2Layout.ExpectedVersion)
                {
                    State = ConnectionState.VersionMismatch;
                    LastError =
                        $"AMS2 meldet Shared-Memory-Version {ReportedVersion}, diese App erwartet " +
                        $"{Ams2Layout.ExpectedVersion}. Die Werte können falsch sein.";
                    return true;
                }

                State = ConnectionState.Connected;
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
            {
                Cleanup();
                LastError = ex.Message;
            }
        }

        State = ConnectionState.GameNotRunning;
        LastError = "AMS2 läuft nicht, oder Shared Memory ist nicht auf \"Project CARS 2\" gestellt.";
        return false;
    }

    /// <summary>
    /// Liest einen konsistenten Snapshot. Gibt <c>false</c> zurück, wenn der Block
    /// gerade beschrieben wird und sich auch nach mehreren Versuchen kein
    /// stabiler Zustand ergibt – dann einfach den nächsten Frame abwarten.
    /// </summary>
    public bool TryRead(out Ams2SharedMemory data)
    {
        if (!_pointerAcquired)
        {
            data = default;
            return false;
        }

        var source = (Ams2SharedMemory*)_pointer;
        ref uint sequence = ref Unsafe.AsRef<uint>(&source->mSequenceNumber);

        for (int attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            uint before = Volatile.Read(ref sequence);
            if ((before & 1) != 0)
            {
                // Ungerade: das Spiel schreibt gerade. Kurz warten statt kopieren.
                Thread.SpinWait(60);
                continue;
            }

            data = *source;

            if (before == Volatile.Read(ref sequence))
            {
                return true;
            }

            Thread.SpinWait(60);
        }

        TornReads++;
        data = default;
        return false;
    }

    /// <summary>Trennt die Verbindung, etwa wenn AMS2 beendet wurde.</summary>
    public void Disconnect()
    {
        Cleanup();
        State = ConnectionState.GameNotRunning;
    }

    private void Cleanup()
    {
        if (_pointerAcquired)
        {
            _view!.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
            _pointer = null;
        }

        _view?.Dispose();
        _view = null;
        _file?.Dispose();
        _file = null;
    }

    public void Dispose() => Cleanup();
}
