namespace DrivingCoach.Telemetry.Ams2;

/// <summary>Werte für <c>SharedMemory.mGameState</c> (Type#1).</summary>
public enum GameState : uint
{
    Exited = 0,
    FrontEnd,
    InGamePlaying,
    InGamePaused,
    InGameInMenuTimeTicking,
    InGameRestarting,
    InGameReplay,
    FrontEndReplay,
}

/// <summary>Werte für <c>SharedMemory.mSessionState</c> (Type#2).</summary>
public enum SessionState : uint
{
    Invalid = 0,
    Practice,
    Test,
    Qualify,
    FormationLap,
    Race,
    TimeAttack,
}

/// <summary>Werte für <c>SharedMemory.mRaceState</c> (Type#3).</summary>
public enum RaceState : uint
{
    Invalid = 0,
    NotStarted,
    Racing,
    Finished,
    Disqualified,
    Retired,
    Dnf,
}

/// <summary>Werte für <c>SharedMemory.mPitMode</c> (Type#7).</summary>
public enum PitMode : uint
{
    None = 0,
    DrivingIntoPits,
    InPit,
    DrivingOutOfPits,
    InGarage,
    DrivingOutOfGarage,
}

/// <summary>Bitmaske für <c>SharedMemory.mCarFlags</c> (Type#9).</summary>
[Flags]
public enum CarFlags : uint
{
    None = 0,
    Headlight = 1 << 0,
    EngineActive = 1 << 1,
    EngineWarning = 1 << 2,
    SpeedLimiter = 1 << 3,
    Abs = 1 << 4,
    Handbrake = 1 << 5,
    Tcs = 1 << 6,
    Scs = 1 << 7,
}

/// <summary>
/// Untergrund je Rad, <c>SharedMemory.mTerrain</c> (Type#11).
/// Nur die für "Reifen abseits der Strecke" relevanten Werte sind benannt; die
/// Reihenfolge entspricht exakt dem Header, deshalb sind auch die unbenannten
/// Zwischenwerte als Rohzahl korrekt interpretierbar.
/// </summary>
public enum Terrain : uint
{
    Road = 0,
    LowGripRoad,
    BumpyRoad1,
    BumpyRoad2,
    BumpyRoad3,
    Marbles,
    GrassyBerms,
    Grass,
    Gravel,
    BumpyGravel,
    RumbleStrips,
    Drains,
    TyreWalls,
    CementWalls,
    GuardRails,
    Sand,
    BumpySand,
    Dirt,
    BumpyDirt,
    DirtRoad,
    BumpyDirtRoad,
    Pavement,
    DirtBank,
    Wood,
    DryVerge,
    ExitRumbleStrips,
    GrassCrete,
    LongGrass,
    SlopeGrass,
    Cobbles,
    SandRoad,
    BakedClay,
    AstroTurf,
    SnowHalf,
    SnowFull,
    DamagedRoad1,
    TrainTrackRoad,
    BumpyCobbles,
    AriesOnly,
    OrionOnly,
    B1Rumbles,
    B2Rumbles,
    RoughSandMedium,
    RoughSandHeavy,
    SnowWalls,
    IceRoad,
    RunoffRoad,
    IllegalStrip,
    PaintConcrete,
    PaintConcreteIllegal,
    RallyTarmac,
}

/// <summary>Hilfsfunktionen zur Bewertung des Untergrunds.</summary>
public static class TerrainExtensions
{
    /// <summary>
    /// True, wenn der Untergrund kein Asphalt/Randstein ist, das Rad also
    /// tatsächlich neben der Strecke läuft.
    /// </summary>
    public static bool IsOffTrack(this Terrain terrain) => terrain switch
    {
        Terrain.Road or Terrain.LowGripRoad or Terrain.BumpyRoad1 or Terrain.BumpyRoad2
            or Terrain.BumpyRoad3 or Terrain.Marbles or Terrain.RumbleStrips or Terrain.Drains
            or Terrain.Pavement or Terrain.ExitRumbleStrips or Terrain.Cobbles
            or Terrain.DamagedRoad1 or Terrain.TrainTrackRoad or Terrain.BumpyCobbles
            or Terrain.B1Rumbles or Terrain.B2Rumbles or Terrain.IllegalStrip
            or Terrain.PaintConcrete or Terrain.PaintConcreteIllegal or Terrain.RallyTarmac
            or Terrain.RunoffRoad => false,
        _ => true,
    };
}
