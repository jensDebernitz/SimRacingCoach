using System.Collections.Concurrent;
using DrivingCoach.Coaching;

namespace DrivingCoach.Ai;

/// <summary>
/// Vorrat an vorformulierten Ansagen, aus dem die Fahrt bedient wird.
/// </summary>
/// <remarks>
/// <para>
/// Der Kern der Lösung für das Latenzproblem: Gefüllt wird am Rundenende aus
/// dem Thread-Pool, gelesen wird während der Fahrt aus dem Telemetrie-Thread.
/// Beim Auslösen einer Ansage findet also nur noch ein Nachschlag in einer
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> statt – keine Anfrage, kein
/// Warten, keine Netzabhängigkeit mitten in der Kurve.
/// </para>
/// <para>
/// Je Schlüssel liegen mehrere Fassungen bereit und werden reihum ausgegeben.
/// Ein Coach, der beim dritten blockierenden Rad wortgleich dasselbe sagt,
/// klingt nach Anrufbeantworter.
/// </para>
/// </remarks>
public sealed class Phrasebook : IPhraseSource
{
    /// <summary>
    /// Längenobergrenze für eine Ansage. Gesprochen sind 90 Zeichen rund vier
    /// Sekunden – länger darf nichts sein, was während der Fahrt kommt.
    /// </summary>
    public const int MaxSpeechLength = 90;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Anzahl belegter Schlüssel. Nur für Anzeige und Tests.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public bool TryGet(string key, out string speech)
    {
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            return entry.TryNext(out speech);
        }

        speech = string.Empty;
        return false;
    }

    /// <summary>
    /// Legt Fassungen ab. Unbrauchbares wird verworfen, nicht gekürzt – ein
    /// mitten im Wort abgeschnittener Satz ist schlimmer als der Standardsatz.
    /// </summary>
    /// <returns>Wie viele Fassungen übernommen wurden.</returns>
    public int Set(string key, IEnumerable<string> variants)
    {
        string[] usable = variants
            .Select(Clean)
            .Where(IsUsable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (usable.Length == 0)
        {
            _entries.TryRemove(key, out _);
            return 0;
        }

        _entries[key] = new Entry(usable);
        return usable.Length;
    }

    /// <summary>Wirft alle Fassungen weg, deren Schlüssel mit dem Präfix beginnt.</summary>
    /// <remarks>
    /// Wird vor dem Befüllen der Kurventipps gebraucht: hat der Fahrer eine
    /// Kurve in der neuen Runde sauber erwischt, darf der alte Tipp dazu nicht
    /// stehen bleiben.
    /// </remarks>
    public void ClearPrefix(string prefix)
    {
        foreach (string key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// Prüft, ob eine Fassung als Ansage taugt. Öffentlich, weil genau diese
    /// Grenze das ist, was die KI vom Fahrer fernhält, wenn sie ausschweift.
    /// </summary>
    public static bool IsUsable(string? speech) =>
        !string.IsNullOrWhiteSpace(speech) &&
        speech.Length <= MaxSpeechLength &&
        speech.AsSpan().IndexOfAny('\n', '\r') < 0 &&

        // Aufzählungszeichen und Sternchen verrät ein Modell, das statt eines
        // Satzes eine Liste geliefert hat. Vorgelesen wird das zu Kauderwelsch.
        !speech.Contains('*', StringComparison.Ordinal) &&
        !speech.StartsWith("- ", StringComparison.Ordinal);

    private static string Clean(string? value) => value?.Trim().Trim('"') ?? string.Empty;

    /// <summary>Die Fassungen zu einem Schlüssel samt Reihum-Zähler.</summary>
    private sealed class Entry(string[] variants)
    {
        private int _next = -1;

        public bool TryNext(out string speech)
        {
            if (variants.Length == 0)
            {
                speech = string.Empty;
                return false;
            }

            // Interlocked statt Sperre: gelesen wird aus dem Telemetrie-Thread,
            // und der darf für nichts anhalten.
            int index = (int)((uint)Interlocked.Increment(ref _next) % (uint)variants.Length);

            speech = variants[index];
            return true;
        }
    }
}
