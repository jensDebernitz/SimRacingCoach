using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using DrivingCoach.Ai;
using DrivingCoach.Coaching;
using DrivingCoach.Overlay.Speech;
using DrivingCoach.Overlay.Updates;
using DrivingCoach.Overlay.ViewModels;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Simulation;
using Velopack;

namespace DrivingCoach.Overlay;

/// <summary>
/// Startpunkt des Overlays. Hier wird entschieden, woher die Telemetrie kommt,
/// und alles zusammengesteckt.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private CoachEngine? _engine;
    private CoachVoice? _voice;
    private OverlayViewModel? _viewModel;
    private AiCoach? _ai;
    private UpdateService? _updates;
    private CancellationTokenSource? _updateCheck;

    /// <summary>
    /// Der Einstiegspunkt des Prozesses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF erzeugt sich normalerweise selbst ein <c>Main</c>. Hier steht es von
    /// Hand da, weil <see cref="VelopackApp"/> als Allererstes laufen muss –
    /// noch vor jedem WPF-Aufruf. Beim Installieren, Aktualisieren und
    /// Deinstallieren startet Velopack die Anwendung mit Sonderargumenten,
    /// erledigt in <c>Run()</c> seine Arbeit und beendet den Prozess wieder.
    /// Ein Fenster darf dabei nie aufgehen.
    /// </para>
    /// <para>
    /// Dazu gehört <c>&lt;StartupObject&gt;</c> in der Projektdatei und die
    /// Umstellung von App.xaml auf <c>Page</c>; sonst gäbe es zwei
    /// <c>Main</c>-Methoden.
    /// </para>
    /// </remarks>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sämtliche Texte sind deutsch, also müssen es die Zahlen darin auch
        // sein. Ohne das stünde auf einem englischen Windows "0.23 s" statt
        // "0,23 s" – und zwar auch auf dem Telemetrie-Thread, weshalb es hier
        // vor dem ersten Thread gesetzt wird und nicht nur für die Oberfläche.
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;
        CultureInfo.CurrentCulture = german;
        CultureInfo.CurrentUICulture = german;

        // Ohne diesen Haken verschwindet ein Fehler wortlos und das Overlay
        // friert scheinbar grundlos ein.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        bool useSimulator = e.Args.Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase));

        ITelemetrySource source = useSimulator
            ? new SimulatedTelemetrySource()
            : new Ams2TelemetrySource();

        OverlaySettings settings = OverlaySettings.Load();

        // Legt beim ersten Start eine kommentierte ai.json an. Ohne sie müsste
        // der Fahrer die Struktur der Datei erraten.
        AiSettings.EnsureTemplate();
        _ai = new AiCoach(AiSettings.Load());

        _voice = new CoachVoice();

        // Der Vorrat wird dem Coach schon im Konstruktor mitgegeben: Er ist
        // anfangs leer, und ein leerer Nachschlag liefert den gerechneten Satz.
        _engine = new CoachEngine(source, new LapStore(), new CoachOptions(), _ai.Phrases);
        _engine.SpeechRequested += OnSpeechRequested;
        _ai.Attach(_engine);

        // Die Anzeige liest dem Aufsteller nur seinen Status ab; gezeichnet
        // wird die Linie im eigenen, bildschirmfüllenden Fenster.
        var presenter = new IdealLinePresenter();

        _viewModel = new OverlayViewModel(_engine, presenter)
        {
            AiStatus = _ai.StatusLine,
        };

        var dialog = new CoachDialog(new CoachDialogViewModel(_ai, _engine));
        var line = new LineOverlayWindow(_engine, presenter);

        var window = new OverlayWindow(_viewModel, _engine, _voice, settings, dialog, line, presenter);
        MainWindow = window;
        window.Show();

        StartUpdateCheck();
    }

    /// <summary>
    /// Sucht nebenher nach einer neuen Version.
    /// </summary>
    /// <remarks>
    /// Bewusst erst nach dem Anzeigen des Fensters und bewusst ohne
    /// <c>await</c>: Steht das Netz, wartet die Anfrage bis zum Zeitlimit, und
    /// solange soll der Fahrer nicht auf sein Overlay warten müssen.
    /// </remarks>
    private void StartUpdateCheck()
    {
        _updates = new UpdateService();
        _updateCheck = new CancellationTokenSource();

        CancellationToken token = _updateCheck.Token;

        _ = Task.Run(
            async () =>
            {
                await _updates.CheckAsync(token).ConfigureAwait(false);

                // Die Statuszeile gehört der Oberfläche; geschrieben wird sie
                // deshalb im UI-Thread und nicht von hier aus.
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_viewModel is { } viewModel)
                    {
                        viewModel.VersionStatus = _updates.StatusLine;
                    }
                });
            },
            token);
    }

    /// <summary>
    /// Wird aus dem Telemetrie-Thread aufgerufen. <see cref="CoachVoice"/> ist
    /// intern gesperrt, deshalb ist kein Umweg über den Dispatcher nötig – und
    /// die Ansage kommt ohne die Verzögerung eines UI-Durchlaufs.
    /// </summary>
    private void OnSpeechRequested(string text) => _voice?.Say(text);

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Im Overlay ist ein Fehler aufgetreten:\n\n{e.Exception}",
            "Driving Coach – Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Läuft gerade noch ein Download, wird er hier abgebrochen – sonst
        // hielte der Hintergrund-Thread das Beenden auf.
        _updateCheck?.Cancel();

        // Zuerst die KI abhängen: Sie lauscht auf LapReviewed und würde sonst
        // noch eine Besprechung für eine Runde anstoßen, die niemand mehr sieht.
        _ai?.Dispose();

        if (_engine is not null)
        {
            _engine.SpeechRequested -= OnSpeechRequested;
            _engine.Stop();
            _engine.Dispose();
        }

        _viewModel?.Dispose();
        _voice?.Dispose();

        // Ganz zum Schluss: Ein fertig geladenes Update wird jetzt angewandt,
        // wo keine Runde mehr dadurch kaputtgeht.
        _updates?.ApplyOnExit();
        _updateCheck?.Dispose();

        base.OnExit(e);
    }
}
