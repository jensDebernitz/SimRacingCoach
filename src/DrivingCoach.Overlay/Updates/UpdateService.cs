using System.Net.Http;
using System.Runtime.Versioning;
using Velopack;
using Velopack.Sources;

namespace DrivingCoach.Overlay.Updates;

/// <summary>
/// Holt neue Versionen im Hintergrund und legt sie bereit.
/// </summary>
/// <remarks>
/// <para>
/// Bewusst <b>ohne</b> Neustart während der Fahrt. Velopack kann eine neue
/// Version sofort anwenden und die Anwendung neu starten – mitten in einer
/// Runde wäre das aber der schlechteste denkbare Moment: Das Overlay wäre weg,
/// die laufende Runde verloren, und wer gerade in Kurve 4 einlenkt, hat dafür
/// kein Verständnis. Deshalb wird nur heruntergeladen; angewandt wird beim
/// Beenden (<see cref="ApplyOnExit"/>).
/// </para>
/// <para>
/// Läuft der Coach aus dem Build-Verzeichnis statt aus einer Installation,
/// tut dieser Dienst gar nichts. <see cref="UpdateManager.IsInstalled"/>
/// unterscheidet beides; ohne die Prüfung liefe beim Entwickeln in jede
/// Ausnahme.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class UpdateService
{
    /// <summary>Wo die Releases liegen. Dasselbe Repository, aus dem gebaut wird.</summary>
    public const string RepositoryUrl = "https://github.com/jensDebernitz/SimRacingCoach";

    private readonly UpdateManager _manager;

    private VelopackAsset? _ready;

    public UpdateService(IUpdateSource? source = null)
        => _manager = new UpdateManager(
            source ?? new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    /// <summary>Die laufende Version, oder <c>null</c> außerhalb einer Installation.</summary>
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Kurzer Satz für die Statuszeile im Overlay.</summary>
    public string StatusLine { get; private set; } = string.Empty;

    /// <summary>
    /// Sucht einmal nach einer neuen Version und lädt sie gegebenenfalls
    /// herunter.
    /// </summary>
    /// <remarks>
    /// Jeder Fehler landet in der Statuszeile statt in einem Dialog. Ein
    /// fehlgeschlagenes Update ist ein Grund, es später nochmal zu versuchen –
    /// kein Grund, den Fahrer aus der Session zu reißen.
    /// </remarks>
    public async Task CheckAsync(CancellationToken token = default)
    {
        if (!_manager.IsInstalled)
        {
            // Aus dem Build-Verzeichnis gestartet: kein Update, aber auch kein
            // Hinweis – das ist der Normalfall beim Entwickeln.
            return;
        }

        StatusLine = $"Version {CurrentVersion}";

        try
        {
            UpdateInfo? update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return;
            }

            StatusLine = $"Version {CurrentVersion} · lädt {update.TargetFullRelease.Version} …";

            await _manager.DownloadUpdatesAsync(update, progress: null, cancelToken: token)
                .ConfigureAwait(false);

            _ready = update.TargetFullRelease;
            StatusLine = $"Version {update.TargetFullRelease.Version} bereit – beim Beenden installiert";
        }
        catch (OperationCanceledException)
        {
            // Der Coach wird beendet, während noch geladen wird. Nichts zu melden.
        }
        catch (Exception ex)
        {
            StatusLine = $"Version {CurrentVersion} · Update nicht erreichbar ({Reason(ex)})";
        }
    }

    /// <summary>
    /// Wendet ein bereitliegendes Update an, sobald der Coach beendet ist.
    /// </summary>
    /// <remarks>
    /// Wird aus dem Beenden heraus aufgerufen. Velopack wartet auf das Ende des
    /// eigenen Prozesses und tauscht die Dateien danach – deshalb darf hier
    /// nichts mehr blockieren.
    /// </remarks>
    public void ApplyOnExit()
    {
        if (_ready is null)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Beim Beenden ist niemand mehr da, der eine Meldung läse. Das
            // Paket liegt weiter auf der Platte und wird beim nächsten Start
            // erneut angeboten.
        }
    }

    private static string Reason(Exception ex) => ex switch
    {
        HttpRequestException => "kein Netz",
        TaskCanceledException => "Zeitüberschreitung",
        _ => ex.GetType().Name,
    };
}
