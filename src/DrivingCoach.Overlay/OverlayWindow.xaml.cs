using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private double _scale = 1.0;

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

    /// <summary>
    /// Holt beide Fenster regelmäßig wieder nach vorn.
    /// </summary>
    /// <remarks>
    /// Ein Sekundentakt genügt: Der Platz geht nur verloren, wenn sich ein
    /// anderes Fenster als "immer oben" anmeldet – beim Start von AMS2 und beim
    /// Wechsel in den Vollbildmodus. Häufiger zu prüfen brächte nichts, seltener
    /// hieße, sekundenlang hinter dem Spiel zu liegen.
    /// </remarks>
    private readonly DispatcherTimer _topmostGuard = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(1.0),
    };

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

        // Wer die Größe noch nie angefasst hat, bekommt eine, die zum
        // Bildschirm passt. Auf 1440 Bildpunkten sind 100 % schlicht zu klein,
        // und niemand sucht nach einer Taste für ein Problem, von dem er
        // annimmt, es sei so gedacht.
        ApplyScale(settings.Scale ?? OverlayScale.ForScreen(SystemParameters.PrimaryScreenHeight));

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

        _topmostGuard.Tick += OnTopmostGuardTick;
        _topmostGuard.Start();

        _viewModel.Start();
        _engine.Start();
    }

    /// <summary>
    /// Sorgt dafür, dass der Coach vor dem Spiel liegt und nicht dahinter.
    /// </summary>
    /// <remarks>
    /// Reihenfolge von unten nach oben: erst das bildschirmfüllende
    /// Linienfenster, dann die Anzeige. Andersherum läge die Linie über der
    /// Anzeige und würde sie verdecken.
    /// </remarks>
    private void OnTopmostGuardTick(object? sender, EventArgs e)
    {
        _line?.KeepOnTop();

        NativeMethods.BringToTop(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Holt das Fenster zurück, wenn es über den Bildschirmrand hinausragt –
    /// weil die gespeicherte Position auf einem inzwischen abgesteckten Monitor
    /// lag, oder weil es gerade vergrößert wurde.
    /// </summary>
    /// <remarks>
    /// Gemessen gegen die tatsächliche Fenstergröße, sobald sie feststeht. Die
    /// frühere Regel ließ pauschal 80 Bildpunkte stehen; das reichte, solange
    /// das Overlay eine feste Breite hatte, aber bei 250 % hinge davon fast
    /// alles über dem Rand. Vor dem ersten Messlauf sind
    /// <see cref="FrameworkElement.ActualWidth"/> und -Height null – dann bleibt
    /// es beim alten Zipfel, weil ein Fenster am falschen Fleck immer noch
    /// besser ist als eines, das gar nicht zu fassen ist.
    /// </remarks>
    private void EnsureOnScreen()
    {
        const double minimumVisible = 80.0;

        double width = ActualWidth > 0 ? ActualWidth : minimumVisible;
        double height = ActualHeight > 0 ? ActualHeight : minimumVisible;

        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;

        Left = Math.Clamp(Left, left, Math.Max(left, left + SystemParameters.VirtualScreenWidth - width));
        Top = Math.Clamp(Top, top, Math.Max(top, top + SystemParameters.VirtualScreenHeight - height));
    }

    /// <summary>
    /// Schreibt die Belegung so in die Hilfe, wie sie tatsächlich zustande kam.
    /// </summary>
    /// <remarks>
    /// Nicht die Wunschbelegung: Ist Strg+Alt+M von einem anderen Programm
    /// besetzt, liegt das Verschieben auf Strg+Alt+V – und dann muss unten auch
    /// Strg+Alt+V stehen. Eine Hilfe, die etwas anderes behauptet als das, was
    /// passiert, ist schlimmer als keine.
    /// </remarks>
    private void UpdateHotkeyHelp()
    {
        var lines = new List<string> { string.Join("  ·  ", _hotkeys.Descriptions) };

        if (_hotkeys.Moved.Count > 0)
        {
            lines.Add($"Belegt von einem anderen Programm, deshalb verlegt: {string.Join(" · ", _hotkeys.Moved)}");
        }

        if (_hotkeys.Failed.Count > 0)
        {
            lines.Add($"Gar keine Taste frei für: {string.Join(", ", _hotkeys.Failed)}");
        }

        _viewModel.HotkeyHelp = string.Join("\n", lines);
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

            case HotkeyAction.ScaleUp:
                ChangeScale(+1);
                break;

            case HotkeyAction.ScaleDown:
                ChangeScale(-1);
                break;
        }
    }

    /// <summary>
    /// Setzt die Größe des Overlays.
    /// </summary>
    /// <remarks>
    /// Bei gebrochenen Faktoren sind die Schriftgrade keine ganzen Zahlen mehr.
    /// <c>Display</c> rastet Glyphen auf ganze Bildpunkte – bei 13 statt 10
    /// Punkt Schriftgröße fallen die Rundungen dann als ungleiche Buchstaben-
    /// abstände auf. <c>Ideal</c> rechnet dafür in Zwischenwerten. Bei
    /// unverändertem Maßstab bleibt es beim schärferen <c>Display</c>.
    /// </remarks>
    private void ApplyScale(double scale)
    {
        _scale = OverlayScale.Clamp(scale);

        RootScale.ScaleX = _scale;
        RootScale.ScaleY = _scale;

        TextOptions.SetTextFormattingMode(
            this,
            Math.Abs(_scale - 1.0) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
    }

    private void ChangeScale(int steps)
    {
        double wanted = OverlayScale.Clamp(_scale + (steps * OverlayScale.Step));

        if (Math.Abs(wanted - _scale) < 0.001)
        {
            // Schon am Anschlag. Ohne die Meldung sähe es aus, als hätte die
            // Taste gar nicht gegriffen.
            _engine.PostExternal(
                CoachMessageKind.Info,
                $"Overlay-Größe bleibt bei {OverlayScale.Percent(_scale)} % – Anschlag erreicht.",
                string.Empty);
            return;
        }

        ApplyScale(wanted);

        // Das Fenster ist gerade gewachsen; am Bildschirmrand könnte es jetzt
        // hinausragen. Erst neu messen lassen, sonst rechnet EnsureOnScreen mit
        // der Breite von vorhin – WPF misst von sich aus erst im nächsten
        // Durchlauf, und dann steht das Fenster schon einen Wimpernschlag falsch.
        UpdateLayout();
        EnsureOnScreen();

        _settings = _settings with { Scale = _scale };
        _settings.Save();

        _engine.PostExternal(
            CoachMessageKind.Info,
            $"Overlay-Größe {OverlayScale.Percent(_scale)} %",
            string.Empty);
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
            Scale = _scale,
        };
        _settings.Save();

        _topmostGuard.Stop();
        _topmostGuard.Tick -= OnTopmostGuardTick;

        _hotkeys.Pressed -= OnHotkey;
        _hotkeys.Dispose();

        // Frage- und Linienfenster werden nur versteckt, nie geschlossen. Ohne
        // diese Aufrufe bliebe die Anwendung mit unsichtbaren Fenstern am Leben.
        _dialog?.Shutdown();
        _line?.Shutdown();

        base.OnClosed(e);
    }
}
