using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Overlay.Interop;
using DrivingCoach.Overlay.Speech;
using DrivingCoach.Overlay.ViewModels;

namespace DrivingCoach.Overlay;

/// <summary>
/// Das Overlay-Fenster: randlos, immer oben und im Normalfall durchklickbar,
/// damit Maus und Lenkrad weiter beim Spiel landen.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly CoachEngine _engine;
    private readonly CoachVoice _voice;
    private readonly HotkeyManager _hotkeys = new();

    private OverlaySettings _settings;
    private bool _isHidden;

    /// <summary>
    /// Das Frage-Fenster. Optional, damit das Overlay auch ohne die KI-Schicht
    /// baubar bleibt; im laufenden Betrieb ist es immer gesetzt. Ohne Schlüssel
    /// öffnet es sich trotzdem – und nennt den Grund, statt stumm zu bleiben.
    /// </summary>
    private readonly CoachDialog? _dialog;

    /// <summary>
    /// Das bildschirmfüllende Fenster mit der Ideallinie. Ebenfalls optional,
    /// damit das Overlay ohne es baubar bleibt.
    /// </summary>
    private readonly LineOverlayWindow? _line;

    /// <summary>Die Kamerawerte der Linie, zum Einmessen.</summary>
    private readonly IdealLinePresenter? _presenter;

    private readonly CalibrationTuner _tuner = new();

    public OverlayWindow(
        OverlayViewModel viewModel,
        CoachEngine engine,
        CoachVoice voice,
        OverlaySettings settings,
        CoachDialog? dialog = null,
        LineOverlayWindow? line = null,
        IdealLinePresenter? presenter = null)
    {
        _viewModel = viewModel;
        _engine = engine;
        _voice = voice;
        _settings = settings;
        _dialog = dialog;
        _line = line;
        _presenter = presenter;

        InitializeComponent();

        DataContext = viewModel;

        Left = settings.Left;
        Top = settings.Top;

        _voice.Enabled = settings.SpeechEnabled;
        _viewModel.SpeechOn = settings.SpeechEnabled;
        _viewModel.ShowCornerReport = settings.ShowCornerReport;
        _viewModel.LineOn = settings.ShowIdealLine;
        _viewModel.SpeechStatus = voice.IsAvailable && voice.IsGerman ? string.Empty : voice.StatusLine;

        _hotkeys.Pressed += OnHotkey;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint handle = new WindowInteropHelper(this).Handle;

        NativeMethods.MakePassiveOverlay(handle);
        NativeMethods.SetClickThrough(handle, true);

        _hotkeys.Attach(this);
        UpdateHotkeyHelp();

        EnsureOnScreen();

        ShowLine(_viewModel.LineOn);

        _viewModel.Start();
        _engine.Start();
    }

    /// <summary>
    /// Holt das Fenster zurück, wenn die gespeicherte Position auf einem
    /// Monitor lag, der inzwischen nicht mehr angeschlossen ist.
    /// </summary>
    private void EnsureOnScreen()
    {
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80;
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80;

        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, maxLeft));
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, maxTop));
    }

    private void UpdateHotkeyHelp()
    {
        string help = string.Join("  ·  ", HotkeyManager.Descriptions);

        _viewModel.HotkeyHelp = _hotkeys.Failed.Count == 0
            ? help
            : $"{help}\nBelegt von einem anderen Programm: {string.Join(", ", _hotkeys.Failed)}";
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleVisibility:
                ToggleVisibility();
                break;

            case HotkeyAction.ToggleMoveMode:
                ToggleMoveMode();
                break;

            case HotkeyAction.ToggleSpeech:
                ToggleSpeech();
                break;

            case HotkeyAction.ToggleLine:
                ToggleLine();
                break;

            case HotkeyAction.ResetReference:
                _engine.ResetReference();
                break;

            case HotkeyAction.AskQuestion:
                _dialog?.Summon();
                break;

            case HotkeyAction.SessionSummary:
                _dialog?.SummonWithSummary();
                break;

            case HotkeyAction.Quit:
                Close();
                break;

            case HotkeyAction.ToggleCalibration:
                ToggleCalibration();
                break;

            case HotkeyAction.CalibrationNext:
                SelectKnob(+1);
                break;

            case HotkeyAction.CalibrationPrevious:
                SelectKnob(-1);
                break;

            case HotkeyAction.CalibrationIncrease:
                TurnKnob(+1);
                break;

            case HotkeyAction.CalibrationDecrease:
                TurnKnob(-1);
                break;
        }
    }

    private void ToggleVisibility()
    {
        _isHidden = !_isHidden;

        // Opacity statt Visibility: ein verstecktes Fenster verliert seinen
        // Handle-Zustand nicht, und die Tastenkürzel bleiben angemeldet.
        Opacity = _isHidden ? 0.0 : 1.0;

        // "Overlay aus" heißt für den Fahrer: freie Sicht. Die Linie gehört
        // dazu, ihre eigene Einstellung bleibt davon aber unberührt.
        ShowLine(!_isHidden && _viewModel.LineOn);

        if (_isHidden && _viewModel.IsMoveMode)
        {
            SetMoveMode(false);
        }
    }

    private void ToggleLine()
    {
        bool enabled = !_viewModel.LineOn;

        _viewModel.LineOn = enabled;
        ShowLine(enabled);

        _settings = _settings with { ShowIdealLine = enabled };
        _settings.Save();
    }

    /// <summary>
    /// Blendet die Linie ein oder aus und holt anschließend die Anzeige wieder
    /// nach vorn.
    /// </summary>
    /// <remarks>
    /// Beide Fenster stehen "immer oben"; unter gleichen Fenstern liegt das
    /// zuletzt gezeigte vorn. Ohne das Zurückholen läge das bildschirmfüllende
    /// Linienfenster über der Anzeige.
    /// </remarks>
    private void ShowLine(bool visible)
    {
        if (_line is null)
        {
            return;
        }

        _line.SetVisible(visible);

        if (visible)
        {
            Topmost = false;
            Topmost = true;
        }
    }

    private void ToggleMoveMode() => SetMoveMode(!_viewModel.IsMoveMode);

    private void SetMoveMode(bool enabled)
    {
        _viewModel.IsMoveMode = enabled;

        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetClickThrough(handle, !enabled);

        if (enabled)
        {
            Opacity = 1.0;
            _isHidden = false;
            return;
        }

        _settings = _settings with { Left = Left, Top = Top };
        _settings.Save();
    }

    /// <summary>
    /// Schaltet das Einmessen ein und aus.
    /// </summary>
    /// <remarks>
    /// Am besten im Stand auf einer Geraden: Dort weiß der Fahrer, wo die
    /// Linie liegen müsste, und kann sie dorthin schieben. In der Kurve ist
    /// nicht zu unterscheiden, ob die Kalibrierung danebenliegt oder die
    /// gerechnete Linie eine andere Spur wählt als erwartet.
    /// </remarks>
    private void ToggleCalibration()
    {
        if (_presenter is null)
        {
            return;
        }

        bool active = !_viewModel.IsCalibrating;
        _viewModel.IsCalibrating = active;

        // Einmessen ohne sichtbare Linie wäre Blindflug.
        if (active && !_viewModel.LineOn)
        {
            ToggleLine();
        }

        ShowCalibration();
    }

    private void SelectKnob(int direction)
    {
        if (!_viewModel.IsCalibrating)
        {
            return;
        }

        _tuner.Select(direction);
        ShowCalibration();
    }

    private void TurnKnob(int steps)
    {
        if (!_viewModel.IsCalibrating || _presenter is null)
        {
            return;
        }

        _presenter.Adjust(_tuner.Apply(_presenter.Calibration, steps));
        ShowCalibration();
    }

    private void ShowCalibration() =>
        _viewModel.CalibrationText = _viewModel.IsCalibrating && _presenter is not null
            ? $"Einmessen · {_tuner.Describe(_presenter.Calibration)}"
            : string.Empty;

    private void ToggleSpeech()
    {
        bool enabled = !_voice.Enabled;

        _voice.Enabled = enabled;
        _viewModel.SpeechOn = enabled;

        if (!enabled)
        {
            _voice.Stop();
        }

        _settings = _settings with { SpeechEnabled = enabled };
        _settings.Save();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!_viewModel.IsMoveMode)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Die Taste wurde zwischen Ereignis und Aufruf losgelassen.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings = _settings with
        {
            Left = Left,
            Top = Top,
            SpeechEnabled = _voice.Enabled,
            ShowCornerReport = _viewModel.ShowCornerReport,
            ShowIdealLine = _viewModel.LineOn,
        };
        _settings.Save();

        _hotkeys.Pressed -= OnHotkey;
        _hotkeys.Dispose();

        // Frage- und Linienfenster werden nur versteckt, nie geschlossen. Ohne
        // diese Aufrufe bliebe die Anwendung mit unsichtbaren Fenstern am Leben.
        _dialog?.Shutdown();
        _line?.Shutdown();

        base.OnClosed(e);
    }
}
