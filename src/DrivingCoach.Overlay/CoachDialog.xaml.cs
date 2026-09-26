using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DrivingCoach.Overlay.ViewModels;

namespace DrivingCoach.Overlay;

/// <summary>
/// Das Fenster für freie Fragen und das Session-Fazit.
/// </summary>
/// <remarks>
/// Anders als das Overlay nimmt dieses Fenster Eingaben an und zieht damit den
/// Fokus von AMS2 weg – anders ließe sich nichts tippen. AMS2 sollte deshalb
/// im randlosen Fenstermodus laufen; im exklusiven Vollbild minimiert Windows
/// das Spiel beim Fokuswechsel.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class CoachDialog : Window
{
    private readonly CoachDialogViewModel _viewModel;

    public CoachDialog(CoachDialogViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
        _viewModel.EntryAdded += OnEntryAdded;
    }

    /// <summary>
    /// Holt das Fenster nach vorn und setzt den Cursor in die Eingabe.
    /// </summary>
    /// <remarks>
    /// Das Fenster wird nie wirklich geschlossen, sondern nur versteckt: So
    /// bleibt der Gesprächsverlauf erhalten, wenn der Fahrer zwischendurch
    /// weiterfährt und später nachhakt.
    /// </remarks>
    public void Summon()
    {
        _viewModel.RefreshSubtitle();

        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Input.Focus();
        Input.SelectAll();
    }

    /// <summary>Zeigt das Fenster und holt zugleich das Fazit der Session.</summary>
    public void SummonWithSummary()
    {
        Summon();
        _ = _viewModel.SummariseAsync();
    }

    /// <summary>Scrollt ans Ende, sobald ein Beitrag dazukommt.</summary>
    /// <remarks>
    /// Über den Dispatcher mit <see cref="DispatcherPriority.Background"/>, weil
    /// die neue Zeile im Moment des Ereignisses noch keine Höhe hat und
    /// <c>ScrollToEnd</c> sonst zu kurz springt.
    /// </remarks>
    private void OnEntryAdded() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Scroller.ScrollToEnd));

    private void OnSendClick(object sender, RoutedEventArgs e) => Send();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        // Umschalt+Eingabe bleibt der Zeilenumbruch – eine Frage über zwei
        // Zeilen soll möglich sein, ohne dass sie beim ersten Umbruch losgeht.
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            return;
        }

        e.Handled = true;
        Send();
    }

    private void Send()
    {
        if (!_viewModel.CanAsk)
        {
            return;
        }

        string question = Input.Text;
        Input.Clear();

        _ = _viewModel.AskAsync(question);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Das Fenster hat keine Titelleiste, also zieht es an jeder freien
        // Stelle mit. Über Eingabefeld und Knopf kommt das Ereignis nicht an.
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Taste war vor dem Aufruf schon wieder los.
        }
    }

    /// <summary>
    /// Fängt das Schließen ab, solange die Anwendung läuft: Sonst wäre das
    /// Fenster nach dem ersten Esc-Druck für immer weg.
    /// </summary>
    public void Shutdown()
    {
        _viewModel.EntryAdded -= OnEntryAdded;
        _viewModel.CancelPending();
        Close();
    }
}
