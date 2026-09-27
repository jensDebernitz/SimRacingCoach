namespace DrivingCoach.Overlay;

/// <summary>
/// Die Größe des Overlays, als Faktor auf die entworfene Grundgröße.
/// </summary>
/// <remarks>
/// Das Overlay wird über eine <c>LayoutTransform</c> skaliert, nicht über
/// größere Schriftgrade: Nur so wachsen Balken, Abstände und Linienstärken im
/// selben Verhältnis mit, und das Fenster misst sich anschließend selbst auf die
/// neue Größe. Es wächst dabei in die Breite – auf einem 34-Zöller ist Platz
/// nach rechts das Einzige, wovon es reichlich gibt.
/// </remarks>
public static class OverlayScale
{
    /// <summary>Kleiner geht nicht – darunter ist die Statuszeile nicht mehr lesbar.</summary>
    public const double Minimum = 0.8;

    /// <summary>Größer auch nicht – sonst deckt das Overlay die Strecke zu.</summary>
    public const double Maximum = 2.5;

    /// <summary>Schrittweite einer Tastenbetätigung.</summary>
    public const double Step = 0.1;

    /// <summary>Bildschirmhöhe, für die die Grundgröße entworfen wurde.</summary>
    private const double DesignHeight = 1080.0;

    /// <summary>Automatisch wird höchstens so weit vergrößert.</summary>
    private const double AutomaticMaximum = 2.0;

    /// <summary>Legt einen Faktor auf ein Vielfaches der Schrittweite und in die Grenzen.</summary>
    public static double Clamp(double scale) =>
        double.IsFinite(scale)
            ? Math.Clamp(Math.Round(scale / Step) * Step, Minimum, Maximum)
            : 1.0;

    /// <summary>
    /// Ein Startwert, der zur Bildschirmhöhe passt – auf 1440 Bildpunkten also
    /// etwa 130 %.
    /// </summary>
    /// <remarks>
    /// Die Höhe ist das bessere Maß als die Breite: Ein 34-Zoll-Breitbild ist
    /// vor allem breiter, nicht höher, und ein Overlay, das nach der Breite
    /// skaliert, würde dort doppelt so groß wie nötig.
    /// </remarks>
    public static double ForScreen(double screenHeight) =>
        screenHeight < 1.0
            ? 1.0
            : Math.Clamp(Math.Round(screenHeight / DesignHeight / Step) * Step, 1.0, AutomaticMaximum);

    /// <summary>Der Faktor als Prozentzahl für die Anzeige.</summary>
    public static int Percent(double scale) => (int)Math.Round(scale * 100.0);
}
