using DrivingCoach.Coaching;
using DrivingCoach.Overlay.Controls;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft den Ringpuffer hinter der Eingabe-Anzeige – vor allem, dass Index 0
/// immer der älteste Wert bleibt, auch nachdem er einmal umgelaufen ist.
/// </summary>
public sealed class InputHistoryTests
{
    [Fact]
    public void Ein_leerer_Puffer_hat_nichts_zu_zeigen()
    {
        var history = new InputHistory(capacity: 4);

        Assert.Equal(0, history.Count);
        Assert.Equal(4, history.Capacity);
    }

    [Fact]
    public void Index_null_ist_der_aelteste_Wert()
    {
        var history = new InputHistory(capacity: 4);

        history.Push(0.1f, 0f, 0f);
        history.Push(0.2f, 0f, 0f);

        Assert.Equal(2, history.Count);
        Assert.Equal(0.1f, history.Throttle(0), precision: 4);
        Assert.Equal(0.2f, history.Throttle(1), precision: 4);
    }

    /// <summary>
    /// Nach dem Umlauf muss der älteste Wert mitwandern. Bliebe er bei Index 0
    /// stehen, spränge die gezeichnete Kurve bei jedem Frame einmal quer durchs
    /// Bild.
    /// </summary>
    [Fact]
    public void Nach_dem_Umlauf_wandert_der_Anfang_mit()
    {
        var history = new InputHistory(capacity: 3);

        for (int i = 1; i <= 5; i++)
        {
            history.Push(i / 10f, 0f, 0f);
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(0.3f, history.Throttle(0), precision: 4);
        Assert.Equal(0.4f, history.Throttle(1), precision: 4);
        Assert.Equal(0.5f, history.Throttle(2), precision: 4);
    }

    [Fact]
    public void Alle_drei_Kanaele_bleiben_beieinander()
    {
        var history = new InputHistory(capacity: 2);

        history.Push(throttle: 0.4f, brake: 0.6f, steering: -0.8f);

        Assert.Equal(0.4f, history.Throttle(0), precision: 4);
        Assert.Equal(0.6f, history.Brake(0), precision: 4);
        Assert.Equal(-0.8f, history.Steering(0), precision: 4);
    }

    /// <summary>
    /// Ohne Referenzrunde gibt es keine Vorgabe. Sie darf dann nicht als 0
    /// abgelegt werden – das hieße "Fuß runter", wo in Wahrheit nichts bekannt
    /// ist. <see cref="float.NaN"/> reißt die gestrichelte Linie stattdessen auf.
    /// </summary>
    [Fact]
    public void Ohne_Vorgabe_bleibt_die_Linie_offen()
    {
        var history = new InputHistory(capacity: 2);

        history.Push(0.5f, 0f, 0f);

        Assert.True(float.IsNaN(history.TargetThrottle(0)));
        Assert.True(float.IsNaN(history.TargetBrake(0)));
    }

    [Fact]
    public void Mit_Vorgabe_steht_sie_neben_dem_Gefahrenen()
    {
        var history = new InputHistory(capacity: 2);

        history.Push(0.5f, 0f, 0f, new PedalTarget(Throttle: 0.9f, Brake: 0.1f));

        Assert.Equal(0.5f, history.Throttle(0), precision: 4);
        Assert.Equal(0.9f, history.TargetThrottle(0), precision: 4);
        Assert.Equal(0.1f, history.TargetBrake(0), precision: 4);
    }

    /// <summary>
    /// Der Fall beim Verlassen der Strecke: Erst gibt es eine Vorgabe, dann
    /// keine mehr. Der alte Wert darf nicht stehen bleiben.
    /// </summary>
    [Fact]
    public void Eine_weggefallene_Vorgabe_bleibt_nicht_stehen()
    {
        var history = new InputHistory(capacity: 2);

        history.Push(0.5f, 0f, 0f, new PedalTarget(0.9f, 0f));
        history.Push(0.5f, 0f, 0f);

        Assert.Equal(0.9f, history.TargetThrottle(0), precision: 4);
        Assert.True(float.IsNaN(history.TargetThrottle(1)));
    }

    [Fact]
    public void Leeren_setzt_alles_zurueck()
    {
        var history = new InputHistory(capacity: 4);

        history.Push(0.5f, 0.5f, 0.5f, new PedalTarget(0.5f, 0.5f));
        history.Clear();

        Assert.Equal(0, history.Count);
    }

    /// <summary>
    /// Nach dem Leeren beginnt der Puffer wieder vorn. Ohne das zurückgesetzte
    /// Schreibfenster läge der erste neue Wert irgendwo in der Mitte.
    /// </summary>
    [Fact]
    public void Nach_dem_Leeren_faengt_der_Puffer_vorn_an()
    {
        var history = new InputHistory(capacity: 3);

        history.Push(0.1f, 0f, 0f);
        history.Push(0.2f, 0f, 0f);
        history.Clear();
        history.Push(0.7f, 0f, 0f);

        Assert.Equal(1, history.Count);
        Assert.Equal(0.7f, history.Throttle(0), precision: 4);
    }

    [Fact]
    public void Jeder_Wert_meldet_sich_bei_der_Anzeige()
    {
        var history = new InputHistory(capacity: 4);
        int updates = 0;
        history.Updated += () => updates++;

        history.Push(0.1f, 0f, 0f);
        history.Push(0.2f, 0f, 0f);
        history.Clear();

        Assert.Equal(3, updates);
    }
}
