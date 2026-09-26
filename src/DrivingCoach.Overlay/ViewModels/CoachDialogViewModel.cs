using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DrivingCoach.Ai;
using DrivingCoach.Coaching;

namespace DrivingCoach.Overlay.ViewModels;

/// <summary>Ein Beitrag im Gesprächsverlauf des Frage-Fensters.</summary>
public sealed record DialogEntry(string Speaker, string Text, Brush Accent);

/// <summary>
/// Der Gesprächsverlauf hinter <c>Strg+Alt+K</c> und <c>Strg+Alt+F</c>.
/// </summary>
/// <remarks>
/// Frage und Fazit teilen sich bewusst ein Fenster: Wer den Trainingsplan
/// gelesen hat, will meistens gleich nachhaken ("wie übe ich Kurve 4?"). Mit
/// zwei getrennten Fenstern wäre der Plan beim Nachfragen nicht mehr zu sehen.
/// </remarks>
public sealed class CoachDialogViewModel : INotifyPropertyChanged
{
    /// <summary>So viele Beiträge bleiben stehen; ältere fallen hinten raus.</summary>
    private const int MaxEntries = 40;

    private static readonly Brush QuestionBrush = Frozen(Color.FromRgb(0x8A, 0x9B, 0xAE));
    private static readonly Brush AnswerBrush = Frozen(Color.FromRgb(0x63, 0xB3, 0xFF));
    private static readonly Brush SummaryBrush = Frozen(Color.FromRgb(0x36, 0xC7, 0x5A));
    private static readonly Brush ProblemBrush = Frozen(Color.FromRgb(0xFF, 0x6B, 0x4A));

    private readonly AiCoach _ai;
    private readonly CoachEngine _engine;

    private CancellationTokenSource? _running;

    public CoachDialogViewModel(AiCoach ai, CoachEngine engine)
    {
        _ai = ai;
        _engine = engine;

        if (!ai.IsConfigured)
        {
            Add("Coach", ai.StatusLine, ProblemBrush);
            Hint = "Ohne Schlüssel bleibt der gerechnete Coach aktiv – nur das Fragen fällt weg.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wird ausgelöst, wenn ein Beitrag dazugekommen ist – zum Nachscrollen.</summary>
    public event Action? EntryAdded;

    public ObservableCollection<DialogEntry> Entries { get; } = [];

    private string _subtitle = string.Empty;
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    private string _hint = "Eingabe fragt · Umschalt+Eingabe macht eine neue Zeile · Strg+Alt+F holt das Fazit";
    public string Hint { get => _hint; private set => Set(ref _hint, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAsk));
            }
        }
    }

    /// <summary>Eingabe und Knopf sind gesperrt, solange eine Anfrage läuft.</summary>
    public bool CanAsk => !IsBusy && _ai.IsConfigured;

    /// <summary>Frischt die Kopfzeile auf – Strecke, Auto und Zahl der Runden.</summary>
    public void RefreshSubtitle()
    {
        CoachState state = _engine.State;
        string session = state.Session.IsUsable
            ? $"{state.Session.TrackDisplayName} · {state.Session.CarName}"
            : "Noch keine Session erkannt";

        Subtitle = $"{session} · {_ai.CollectedLaps} ausgewertete Runden · {_ai.StatusLine}";
    }

    /// <summary>Stellt eine freie Frage.</summary>
    public Task AskAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Task.CompletedTask;
        }

        Add("Du", question.Trim(), QuestionBrush);

        return RunAsync(
            token => _ai.AskAsync(question, _engine.State, token),
            speaker: "Coach",
            accent: AnswerBrush);
    }

    /// <summary>Holt das Fazit der laufenden Session samt Trainingsplan.</summary>
    public Task SummariseAsync() => RunAsync(
        _ai.SummariseSessionAsync,
        speaker: "Fazit der Session",
        accent: SummaryBrush);

    /// <summary>
    /// Führt eine Anfrage aus und hängt das Ergebnis an den Verlauf.
    /// </summary>
    /// <remarks>
    /// Es läuft immer nur eine Anfrage: Die vorige wird abgebrochen, weil sonst
    /// zwei Antworten in unbestimmter Reihenfolge im Verlauf landen würden.
    /// </remarks>
    private async Task RunAsync(Func<CancellationToken, Task<string>> request, string speaker, Brush accent)
    {
        var source = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _running, source);
        previous?.Cancel();
        previous?.Dispose();

        IsBusy = true;

        try
        {
            string answer = await request(source.Token).ConfigureAwait(true);

            if (!source.IsCancellationRequested && answer.Length > 0)
            {
                Add(speaker, answer, accent);
            }
        }
        catch (OperationCanceledException)
        {
            // Nachgefragt, bevor die Antwort da war – die alte ist hinfällig.
            // Netz- und Dienstfehler kommen nicht hier an: der GeminiClient
            // übersetzt sie in einen Klartextgrund, der als Antwort erscheint.
        }
        finally
        {
            // Nur aufräumen, wenn seither keine neue Anfrage gestartet wurde –
            // sonst würde deren Sperre hier fälschlich wieder aufgehoben.
            if (Interlocked.CompareExchange(ref _running, null, source) == source)
            {
                IsBusy = false;
            }

            source.Dispose();
        }
    }

    /// <summary>Bricht eine laufende Anfrage ab, etwa beim Schließen des Fensters.</summary>
    public void CancelPending()
    {
        CancellationTokenSource? running = Interlocked.Exchange(ref _running, null);
        running?.Cancel();
        running?.Dispose();
        IsBusy = false;
    }

    private void Add(string speaker, string text, Brush accent)
    {
        Entries.Add(new DialogEntry(speaker, text, accent));

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }

        EntryAdded?.Invoke();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
