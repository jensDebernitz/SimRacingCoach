using System.Runtime.InteropServices;

namespace DrivingCoach.Telemetry.Ams2;

/// <summary>
/// 1:1-Portierung von <c>SharedMemory.h</c> (Automobilista 2, SHARED_MEMORY_VERSION 14).
/// Die Original-Feldnamen aus dem Header sind bewusst beibehalten, damit sich das
/// Layout Zeile für Zeile gegen <c>reference/SharedMemory.h</c> prüfen lässt.
/// Ein einziges verschobenes Feld macht alle nachfolgenden Werte unbrauchbar –
/// deshalb validiert <see cref="Ams2Layout"/> die Struktur beim Start.
/// C++-<c>bool</c> ist 1 Byte und wird hier als <see cref="byte"/> abgebildet,
/// damit das Struct blittable bleibt.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ParticipantInfo
{
    public byte mIsActive;
    public Byte64 mName;
    public Float3 mWorldPosition;
    public float mCurrentLapDistance;
    public uint mRacePosition;
    public uint mLapsCompleted;
    public uint mCurrentLap;
    public int mCurrentSector;
}

/// <inheritdoc cref="ParticipantInfo"/>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Ams2SharedMemory
{
    // --- Version Number ---
    public uint mVersion;
    public uint mBuildVersionNumber;

    // --- Game States ---
    public uint mGameState;
    public uint mSessionState;
    public uint mRaceState;

    // --- Participant Info ---
    public int mViewedParticipantIndex;
    public int mNumParticipants;
    public ParticipantArray mParticipantInfo;

    // --- Unfiltered Input (rohe Pedal-/Lenkstellung des Fahrers) ---
    public float mUnfilteredThrottle;
    public float mUnfilteredBrake;
    public float mUnfilteredSteering;
    public float mUnfilteredClutch;

    // --- Vehicle information ---
    public Byte64 mCarName;
    public Byte64 mCarClassName;

    // --- Event information ---
    public uint mLapsInEvent;
    public Byte64 mTrackLocation;
    public Byte64 mTrackVariation;
    public float mTrackLength;

    // --- Timings ---
    public int mNumSectors;
    public byte mLapInvalidated;
    public float mBestLapTime;
    public float mLastLapTime;
    public float mCurrentTime;
    public float mSplitTimeAhead;
    public float mSplitTimeBehind;
    public float mSplitTime;
    public float mEventTimeRemaining;
    public float mPersonalFastestLapTime;
    public float mWorldFastestLapTime;
    public float mCurrentSector1Time;
    public float mCurrentSector2Time;
    public float mCurrentSector3Time;
    public float mFastestSector1Time;
    public float mFastestSector2Time;
    public float mFastestSector3Time;
    public float mPersonalFastestSector1Time;
    public float mPersonalFastestSector2Time;
    public float mPersonalFastestSector3Time;
    public float mWorldFastestSector1Time;
    public float mWorldFastestSector2Time;
    public float mWorldFastestSector3Time;

    // --- Flags ---
    public uint mHighestFlagColour;
    public uint mHighestFlagReason;

    // --- Pit Info ---
    public uint mPitMode;
    public uint mPitSchedule;

    // --- Car State ---
    public uint mCarFlags;
    public float mOilTempCelsius;
    public float mOilPressureKPa;
    public float mWaterTempCelsius;
    public float mWaterPressureKPa;
    public float mFuelPressureKPa;
    public float mFuelLevel;
    public float mFuelCapacity;
    public float mSpeed;
    public float mRpm;
    public float mMaxRPM;
    public float mBrake;
    public float mThrottle;
    public float mClutch;
    public float mSteering;
    public int mGear;
    public int mNumGears;
    public float mOdometerKM;
    public byte mAntiLockActive;
    public int mLastOpponentCollisionIndex;
    public float mLastOpponentCollisionMagnitude;
    public byte mBoostActive;
    public float mBoostAmount;

    // --- Motion & Device Related ---
    public Float3 mOrientation;
    public Float3 mLocalVelocity;
    public Float3 mWorldVelocity;
    public Float3 mAngularVelocity;
    public Float3 mLocalAcceleration;
    public Float3 mWorldAcceleration;
    public Float3 mExtentsCentre;

    // --- Wheels / Tyres ---
    public UInt4 mTyreFlags;
    public UInt4 mTerrain;
    public Float4 mTyreY;
    public Float4 mTyreRPS;
    public Float4 mTyreSlipSpeed;          // OBSOLETE laut Header
    public Float4 mTyreTemp;
    public Float4 mTyreGrip;               // OBSOLETE laut Header
    public Float4 mTyreHeightAboveGround;
    public Float4 mTyreLateralStiffness;   // OBSOLETE laut Header
    public Float4 mTyreWear;
    public Float4 mBrakeDamage;
    public Float4 mSuspensionDamage;
    public Float4 mBrakeTempCelsius;
    public Float4 mTyreTreadTemp;
    public Float4 mTyreLayerTemp;
    public Float4 mTyreCarcassTemp;
    public Float4 mTyreRimTemp;
    public Float4 mTyreInternalAirTemp;

    // --- Car Damage ---
    public uint mCrashState;
    public float mAeroDamage;
    public float mEngineDamage;

    // --- Weather ---
    public float mAmbientTemperature;
    public float mTrackTemperature;
    public float mRainDensity;
    public float mWindSpeed;
    public float mWindDirectionX;
    public float mWindDirectionY;
    public float mCloudBrightness;

    // --- PCars2 additions, version 8 ---
    /// <summary>
    /// Ungerade während das Spiel schreibt, gerade wenn der Block ruht.
    /// Grundlage der Tear-Detection in <see cref="SharedMemoryReader"/>.
    /// </summary>
    public uint mSequenceNumber;

    public Float4 mWheelLocalPositionY;
    public Float4 mSuspensionTravel;
    public Float4 mSuspensionVelocity;
    public Float4 mAirPressure;
    public float mEngineSpeed;
    public float mEngineTorque;
    public Float2 mWings;
    public float mHandBrake;

    public Float64 mCurrentSector1Times;
    public Float64 mCurrentSector2Times;
    public Float64 mCurrentSector3Times;
    public Float64 mFastestSector1Times;
    public Float64 mFastestSector2Times;
    public Float64 mFastestSector3Times;
    public Float64 mFastestLapTimes;
    public Float64 mLastLapTimes;
    public Byte64 mLapsInvalidated;        // bool[64]
    public UInt64Array mRaceStates;
    public UInt64Array mPitModes;
    public Float192 mOrientations;         // float[64][3]
    public Float64 mSpeeds;
    public Byte64x64 mCarNames;
    public Byte64x64 mCarClassNames;

    public int mEnforcedPitStopLap;
    public Byte64 mTranslatedTrackLocation;
    public Byte64 mTranslatedTrackVariation;
    public float mBrakeBias;
    public float mTurboBoostPressure;
    public Byte4x40 mTyreCompound;
    public UInt64Array mPitSchedules;
    public UInt64Array mHighestFlagColours;
    public UInt64Array mHighestFlagReasons;
    public UInt64Array mNationalities;
    public float mSnowDensity;

    // --- AMS2 Additions (v10...) ---
    public float mSessionDuration;
    public int mSessionAdditionalLaps;

    public Float4 mTyreTempLeft;
    public Float4 mTyreTempCenter;
    public Float4 mTyreTempRight;

    public uint mDrsState;

    public Float4 mRideHeight;

    public uint mJoyPad0;
    public uint mDPad;

    public int mAntiLockSetting;
    public int mTractionControlSetting;

    public int mErsDeploymentMode;
    public byte mErsAutoModeEnabled;

    public float mClutchTemp;
    public float mClutchWear;
    public byte mClutchOverheated;
    public byte mClutchSlipping;

    public int mYellowFlagState;

    public byte mSessionIsPrivate;
    public int mLaunchStage;
}
