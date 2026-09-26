using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DrivingCoach.Coaching;
using DrivingCoach.Overlay.Interop;

namespace DrivingCoach.Overlay;

/// <summary>
/// Das bildschirmfüllende Fenster, auf dem die Ideallinie liegt.
/// </summary>
/// <remarks>
/// <para>
/// Ein eigenes Fenster und nicht Teil der Anzeige: Die Linie muss über dem
/// ganzen Bild liegen, die Anzeige soll eine verschiebbare Kachel in einer Ecke
/// bleiben. Beides in einem Fenster hieße, die Kachel über den ganzen
/// Bildschirm zu spannen – und damit jeden Mausklick abzufangen.
/// </para>
/// <para>
/// AMS2 muss dafür im randlosen Fenstermodus laufen. Über einem exklusiven
/// Vollbild zeigt Windows kein zweites Fenster an.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class LineOverlayWindow : Window
{
    private readonly CoachEngine _engine;
    private readonly IdealLinePresenter _presenter;

    private bool _isDrawing;

    public LineOverlayWindow(CoachEngine engine, IdealLinePresenter presenter)
    {
        _engine = engine;
        _presenter = presenter;

        InitializeComponent();

        CoverPrimaryScreen();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint handle = new WindowInteropHelper(this).Handle;

        // Anders als die Anzeige kennt dieses Fenster keinen Verschiebemodus:
        // Es liegt über dem ganzen Bildschirm, also müssen Maus und Tastatur
        // hier immer hindurchfallen.
        NativeMethods.MakePassiveOverlay(handle);
        NativeMethods.SetClickThrough(handle, true);
    }

    /// <summary>Blendet die Linie ein oder aus.</summary>
    public void SetVisible(bool visible)
    {
        if (visible)
        {
            Show();
            StartDrawing();
            return;
        }

        StopDrawing();

        // Ein leeres Bild hinterherschicken: Sonst stünde beim nächsten
        // Einblenden für einen Augenblick noch das Band von vorhin da.
        Band.Show([]);
        Hide();
    }

    /// <summary>
    /// Beansprucht den Platz ganz oben neu, falls das Spiel ihn übernommen hat.
    /// </summary>
    /// <remarks>
    /// Wird von der Anzeige getaktet und nicht von hier aus: Beide Fenster
    /// müssen in einer festen Reihenfolge nach oben, sonst legt sich dieses
    /// bildschirmfüllende Fenster über die Anzeige.
    /// </remarks>
    public void KeepOnTop()
    {
        if (!IsVisible)
        {
            return;
        }

        NativeMethods.BringToTop(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Beendet das Fenster endgültig, beim Schließen der Anwendung.</summary>
    public void Shutdown()
    {
        StopDrawing();
        Close();
    }

    /// <summary>
    /// Legt das Fenster über den Hauptbildschirm.
    /// </summary>
    /// <remarks>
    /// Die Perspektive gilt für genau ein Bild und damit für genau einen
    /// Monitor: Der Blickwinkel, aus dem gerechnet wird, ist der des
    /// Spielfensters. Über zwei Bildschirme gespannt wäre er auf beiden falsch.
    /// </remarks>
    private void CoverPrimaryScreen()
    {
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    /// <summary>
    /// Hängt sich an den Zeichentakt der Oberfläche.
    /// </summary>
    /// <remarks>
    /// Nicht an einen eigenen Zeitgeber wie der Rest der Anzeige: Die läuft mit
    /// 30 Hz, weil sich Zahlen und Balken damit flüssig genug ändern. Eine
    /// Linie, die auf dem Asphalt liegen bleiben soll, muss sich dagegen mit
    /// jedem Bild bewegen – bei 30 Hz zittert sie sichtbar über die Strecke.
    /// </remarks>
    private void StartDrawing()
    {
        if (_isDrawing)
        {
            return;
        }

        _isDrawing = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopDrawing()
    {
        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e) =>
        Band.Show(_presenter.Build(_engine.State, (float)Band.ActualWidth, (float)Band.ActualHeight));
}
