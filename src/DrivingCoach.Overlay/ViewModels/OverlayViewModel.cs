using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Overlay.Controls;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Overlay.ViewModels;

/// <summary>Eine Coach-Meldung, fertig für die Anzeige.</summary>
public sealed record MessageItem(string Text, Brush Accent, double At);

/// <summary>Eine Zeile des Kurvenberichts nach einer Runde.</summary>
public sealed record CornerReportItem(string Corner, string Text, string TimeLost, Brush Accent);

/// <summary>
/// Bereitet den Zustand des Coaches für die Anzeige auf.
/// </summary>
/// <remarks>
/// Der Zustand wird bei 30 Hz abgefragt statt per Ereignis geliefert: die
/// Telemetrie läuft mit 120 Hz, und viermal so viele Dispatcher-Durchläufe
/// wie nötig kosten Bildrate im Spiel. Gemeldet werden nur die Ereignisse,
/// die man nicht verpassen darf – Meldungen und Ansagen.
/// </remarks>
public sealed class OverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly Brush InfoBrush = Frozen(Color.FromRgb(0x8A, 0x9B, 0xAE));
    private static readonly Brush FaultBrush = Frozen(Color.FromRgb(0xFF, 0x6B, 0x4A));
    private static readonly Brush CornerTipBrush = Frozen(Color.FromRgb(0xFF, 0xC4, 0x3D));
    private static readonly Brush SummaryBrush = Frozen(Color.FromRgb(0x63, 0xB3, 0xFF));
    private static readonly Brush AchievementBrush = Frozen(Color.FromRgb(0x36, 0xC7, 0x5A));

    /// <summary>Nach dieser Zeit verschwindet eine Meldung wieder.</summary>
    private const double MessageLifetimeSeconds = 25.0;

    private const int MaxMessages = 4;

    private readonly CoachEngine _engine;

    /// <summary>
    /// Optional: Ohne ihn zeigt die Anzeige keinen Hinweis zur Ideallinie –
    /// gezeichnet wird sie ohnehin im eigenen Fenster.
    /// </summary>
    private readonly IdealLinePresenter? _line;

    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<CoachMessage> _incoming = new();

    private LapAnalysis? _shownAnalysis;

    public OverlayViewModel(CoachEngine engine, IdealLinePresenter? line = null)
    {
        _engine = engine;
        _line = line;
        _engine.MessageRaised += OnMessageRaised;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / 30.0),
        };
        _timer.Tick += OnTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Verlauf der Pedale und der Lenkung für die Eingabe-Anzeige.</summary>
    public InputHistory Inputs { get; } = new();

    public ObservableCollection<MessageItem> Messages { get; } = [];

    public ObservableCollection<CornerReportItem> CornerReport { get; } = [];

    public void Start() => _timer.Start();

    #region Statuszeile

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    private string _statusHeadline = "Startet …";
    public string StatusHeadline { get => _statusHeadline; private set => Set(ref _statusHeadline, value); }

    private string _statusDetail = string.Empty;
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }

    private string _sessionLine = string.Empty;
    public string SessionLine { get => _sessionLine; private set => Set(ref _sessionLine, value); }

    #endregion

    #region Delta

    private bool _deltaIsActive;
    public bool DeltaIsActive { get => _deltaIsActive; private set => Set(ref _deltaIsActive, value); }

    private double _delta;
    public double Delta { get => _delta; private set => Set(ref _delta, value); }

    private string _deltaText = "--,--";
    public string DeltaText { get => _deltaText; private set => Set(ref _deltaText, value); }

    private bool _deltaIsFaster;
    public bool DeltaIsFaster { get => _deltaIsFaster; private set => Set(ref _deltaIsFaster, value); }

    private string _referenceHint = "Keine Referenzrunde – fahr eine saubere Runde.";
    public string ReferenceHint { get => _referenceHint; private set => Set(ref _referenceHint, value); }

    #endregion

    #region Zeiten

    private string _currentLapText = "--:--,---";
    public string CurrentLapText { get => _currentLapText; private set => Set(ref _currentLapText, value); }

    private string _predictedLapText = "--:--,---";
    public string PredictedLapText { get => _predictedLapText; private set => Set(ref _predictedLapText, value); }

    private string _referenceLapText = "--:--,---";
    public string ReferenceLapText { get => _referenceLapText; private set => Set(ref _referenceLapText, value); }

    #endregion

    #region Fahrzeug

    private string _speedText = "0";
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

    private string _gearText = "N";
    public string GearText { get => _gearText; private set => Set(ref _gearText, value); }

    private double _rpmFraction;
    public double RpmFraction { get => _rpmFraction; private set => Set(ref _rpmFraction, value); }

    private bool _shouldShift;
    public bool ShouldShift { get => _shouldShift; private set => Set(ref _shouldShift, value); }

    #endregion

    #region Kurve voraus

    private bool _hasUpcomingCorner;
    public bool HasUpcomingCorner { get => _hasUpcomingCorner; private set => Set(ref _hasUpcomingCorner, value); }

    private string _upcomingCornerText = string.Empty;
    public string UpcomingCornerText { get => _upcomingCornerText; private set => Set(ref _upcomingCornerText, value); }

    #endregion

    #region Bedienzustand

    private bool _isMoveMode;
    public bool IsMoveMode { get => _isMoveMode; set => Set(ref _isMoveMode, value); }

    private bool _speechOn = true;
    public bool SpeechOn { get => _speechOn; set => Set(ref _speechOn, value); }

    private string _speechStatus = string.Empty;
    public string SpeechStatus { get => _speechStatus; set => Set(ref _speechStatus, value); }

    private bool _showCornerReport = true;
    public bool ShowCornerReport { get => _showCornerReport; set => Set(ref _showCornerReport, value); }

    private bool _lineOn = true;
    public bool LineOn { get => _lineOn; set => Set(ref _lineOn, value); }

    /// <summary>Warum die Ideallinie gerade nicht auf der Strecke liegt.</summary>
    private string _lineStatus = string.Empty;
    public string LineStatus { get => _lineStatus; private set => Set(ref _lineStatus, value); }

    private bool _isCalibrating;
    public bool IsCalibrating { get => _isCalibrating; set => Set(ref _isCalibrating, value); }

    /// <summary>Der Wert, der beim Einmessen gerade an den Pfeiltasten hängt.</summary>
    private string _calibrationText = string.Empty;
    public string CalibrationText { get => _calibrationText; set => Set(ref _calibrationText, value); }

    private string _hotkeyHelp = string.Empty;
    public string HotkeyHelp { get => _hotkeyHelp; set => Set(ref _hotkeyHelp, value); }

    private string _reportHeader = "Letzte Runde";
    public string ReportHeader { get => _reportHeader; private set => Set(ref _reportHeader, value); }

    /// <summary>Ob die KI überhaupt erreichbar ist – leer heißt: Zeile ausblenden.</summary>
    private string _aiStatus = string.Empty;
    public string AiStatus { get => _aiStatus; set => Set(ref _aiStatus, value); }

    /// <summary>
    /// Laufende Version und Stand der Update-Suche. Leer, solange der Coach
    /// nicht aus einer Installation läuft – beim Entwickeln gibt es keine
    /// Version, über die zu reden wäre.
    /// </summary>
    private string _versionStatus = string.Empty;
    public string VersionStatus { get => _versionStatus; set => Set(ref _versionStatus, value); }

    #endregion

    /// <summary>
    /// Meldungen kommen aus dem Telemetrie-Thread. Sie werden nur eingereiht;
    /// der Timer übernimmt sie im UI-Thread. Ein
    /// <c>Dispatcher.BeginInvoke</c> je Meldung würde bei einer Serie von
    /// Fahrfehlern den UI-Thread unnötig zerhacken.
    /// </summary>
    private void OnMessageRaised(CoachMessage message) => _incoming.Enqueue(message);

    private void OnTick(object? sender, EventArgs e)
    {
        CoachState state = _engine.State;

        // Frame und Delta werden vom Telemetrie-Thread geschrieben, ohne Sperre.
        // Für die Anzeige ist das in Ordnung: schlimmstenfalls zeigt ein Feld
        // für 33 ms den Wert des Vorgänger-Frames.
        TelemetryFrame frame = state.Frame;

        UpdateStatus(state);
        UpdateDelta(state, in frame);
        UpdateTimes(state, in frame);
        UpdateCar(in frame);
        UpdateUpcomingCorner(state, in frame);
        UpdateReport(state);
        UpdateLine();

        Inputs.Push(frame.Throttle, frame.Brake, frame.Steering);

        DrainMessages(frame.Timestamp);
    }

    private void UpdateStatus(CoachState state)
    {
        TelemetryStatus status = state.Status;

        IsConnected = status.IsConnected;
        StatusHeadline = status.Headline;
        StatusDetail = status.Detail ?? string.Empty;

        SessionInfo session = state.Session;
        SessionLine = session.IsUsable
            ? $"{session.TrackDisplayName} · {session.CarName}"
            : string.Empty;
    }

    private void UpdateDelta(CoachState state, in TelemetryFrame frame)
    {
        DeltaState delta = state.Delta;
        bool active = delta.HasReference && delta.IsMeaningful && frame.IsDriving;

        DeltaIsActive = active;
        Delta = active ? delta.Delta : 0.0;
        DeltaText = active ? delta.Delta.ToString("+0.00;-0.00;0.00", De) : "--,--";
        DeltaIsFaster = active && delta.Delta < 0f;

        ReferenceHint = state.Reference is null
            ? "Keine Referenzrunde – fahr eine saubere Runde."
            : $"Referenz · {state.Corners.Count} Kurven · {state.ValidLapCount} gültige Runden";
    }

    private void UpdateTimes(CoachState state, in TelemetryFrame frame)
    {
        CurrentLapText = CoachEngine.FormatLapTime(frame.CurrentLapTime);

        DeltaState delta = state.Delta;
        PredictedLapText = delta.HasReference && delta.IsMeaningful
            ? CoachEngine.FormatLapTime(delta.PredictedLapTime)
            : "--:--,---";

        ReferenceLapText = state.Reference is { } reference
            ? CoachEngine.FormatLapTime(reference.LapTime)
            : "--:--,---";
    }

    private void UpdateCar(in TelemetryFrame frame)
    {
        SpeedText = ((int)MathF.Round(frame.SpeedKmh)).ToString(De);

        GearText = frame.Gear switch
        {
            < 0 => "R",
            0 => "N",
            var gear => gear.ToString(De),
        };

        double fraction = Math.Clamp(frame.RpmFraction, 0f, 1f);
        RpmFraction = fraction;

        // Ab 97 % Maximaldrehzahl bringt Hochdrehen nichts mehr – im obersten
        // Gang wäre der Hinweis allerdings sinnlos.
        ShouldShift = fraction > 0.97 && frame.Gear > 0 && frame.Gear < frame.NumGears;
    }

    private void UpdateUpcomingCorner(CoachState state, in TelemetryFrame frame)
    {
        Corner? corner = state.UpcomingCorner;

        if (corner is null || state.Reference is null || !frame.IsDriving)
        {
            HasUpcomingCorner = false;
            UpcomingCornerText = string.Empty;
            return;
        }

        float metresToBrake = corner.BrakingStartBin * state.Reference.BinSize - frame.LapDistance;

        HasUpcomingCorner = true;
        UpcomingCornerText = metresToBrake > 0f
            ? $"{corner.Name} · {corner.SpeedCategory} · Scheitel {corner.ApexSpeedKmh:0} km/h · in {metresToBrake:0} m"
            : $"{corner.Name} · Scheitel {corner.ApexSpeedKmh:0} km/h";
    }

    /// <summary>
    /// Sagt, warum die Linie fehlt.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Hinweis sucht der Fahrer den Fehler bei sich: Die Linie
    /// braucht mehrere Runden, bis sie zum ersten Mal erscheint, und bis dahin
    /// sieht ein eingeschaltetes Overlay genauso aus wie ein kaputtes.
    /// </remarks>
    private void UpdateLine() =>
        LineStatus = LineOn ? _line?.Status ?? string.Empty : "Ideallinie aus";

    private void UpdateReport(CoachState state)
    {
        LapAnalysis? analysis = state.LastAnalysis;

        if (ReferenceEquals(analysis, _shownAnalysis))
        {
            return;
        }

        _shownAnalysis = analysis;
        CornerReport.Clear();

        if (analysis is null)
        {
            ReportHeader = "Letzte Runde";
            return;
        }

        ReportHeader =
            $"Letzte Runde {CoachEngine.FormatLapTime(analysis.LapTime)} · {analysis.LapDelta.ToString("+0.00;-0.00;0.00", De)} s";

        foreach (CornerAdvice advice in analysis.WorstFirst.Take(3))
        {
            CornerReport.Add(new CornerReportItem(
                advice.Corner.Name,
                StripCornerPrefix(advice.Text, advice.Corner.Name),
                advice.TimeLostLabel,
                CornerTipBrush));
        }

        if (CornerReport.Count == 0)
        {
            CornerReport.Add(new CornerReportItem(
                "✓",
                "Sauber gefahren – keine auffällige Kurve.",
                string.Empty,
                AchievementBrush));
        }
    }

    /// <summary>
    /// Der Kurvenname steht schon in der eigenen Spalte; ihn im Fließtext zu
    /// wiederholen kostet nur Platz.
    /// </summary>
    private static string StripCornerPrefix(string text, string cornerName)
    {
        if (!text.StartsWith(cornerName, StringComparison.Ordinal))
        {
            return text;
        }

        return text[cornerName.Length..].TrimStart(':', ' ', '·', '–', '-');
    }

    private void DrainMessages(double now)
    {
        while (_incoming.TryDequeue(out CoachMessage? message))
        {
            Messages.Insert(0, new MessageItem(message.Text, AccentFor(message.Kind), message.At));

            while (Messages.Count > MaxMessages)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (now - Messages[i].At > MessageLifetimeSeconds)
            {
                Messages.RemoveAt(i);
            }
        }
    }

    private static Brush AccentFor(CoachMessageKind kind) => kind switch
    {
        CoachMessageKind.Fault => FaultBrush,
        CoachMessageKind.CornerTip => CornerTipBrush,
        CoachMessageKind.LapSummary => SummaryBrush,
        CoachMessageKind.Achievement => AchievementBrush,
        _ => InfoBrush,
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _engine.MessageRaised -= OnMessageRaised;
    }
}
