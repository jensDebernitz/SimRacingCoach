using DrivingCoach.Ai;
using DrivingCoach.Coaching;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft den Vorrat, aus dem die Live-Ansagen bedient werden.
/// </summary>
/// <remarks>
/// Der Vorrat ist die Stelle, an der die KI die Fahrt erreicht – und die
/// einzige, an der ein zu langer oder kaputter Satz mitten in der Kurve landen
/// könnte. Deshalb steht die Filterung hier unter Test, nicht die Anfrage.
/// </remarks>
public class PhrasebookTests
{
    [Fact]
    public void Ohne_Eintrag_bleibt_der_gerechnete_Satz_stehen()
    {
        var book = new Phrasebook();

        Assert.False(book.TryGet(PhraseKeys.Corner(3), out string speech));
        Assert.Equal(string.Empty, speech);
    }

    [Fact]
    public void Fassungen_werden_reihum_ausgegeben()
    {
        var book = new Phrasebook();
        book.Set(PhraseKeys.Corner(1), ["Erste.", "Zweite.", "Dritte."]);

        string[] heard =
        [
            Next(book), Next(book), Next(book), Next(book),
        ];

        // Drei verschiedene, und dann fängt es wieder von vorn an.
        Assert.Equal(3, heard.Take(3).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(heard[0], heard[3]);
    }

    [Fact]
    public void Zu_lange_Saetze_werden_verworfen_statt_gekuerzt()
    {
        var book = new Phrasebook();
        string tooLong = new('a', Phrasebook.MaxSpeechLength + 1);

        int accepted = book.Set(PhraseKeys.Corner(2), [tooLong, "Später einlenken."]);

        Assert.Equal(1, accepted);
        Assert.True(book.TryGet(PhraseKeys.Corner(2), out string speech));
        Assert.Equal("Später einlenken.", speech);
    }

    /// <summary>
    /// Ein Modell, das statt eines Satzes eine Aufzählung liefert, darf nicht
    /// vorgelesen werden – "Sternchen später einlenken" hilft niemandem.
    /// </summary>
    [Theory]
    [InlineData("- Später einlenken")]
    [InlineData("**Später** einlenken")]
    [InlineData("Später einlenken.\nDann Gas.")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unbrauchbares_wird_aussortiert(string? variant)
    {
        Assert.False(Phrasebook.IsUsable(variant));

        var book = new Phrasebook();
        Assert.Equal(0, book.Set(PhraseKeys.Corner(4), [variant ?? string.Empty]));
        Assert.False(book.TryGet(PhraseKeys.Corner(4), out _));
    }

    /// <summary>
    /// Bleibt ein alter Kurventipp stehen, beschallt der Coach eine Kurve, die
    /// der Fahrer diese Runde sauber erwischt hat.
    /// </summary>
    [Fact]
    public void Kurventipps_lassen_sich_wegwerfen_ohne_die_Fehlersaetze_zu_treffen()
    {
        var book = new Phrasebook();
        book.Set(PhraseKeys.Corner(1), ["Später einlenken."]);
        book.Set(PhraseKeys.Fault(FaultKind.Lockup), ["Bremsdruck raus."]);

        book.ClearPrefix("corner:");

        Assert.False(book.TryGet(PhraseKeys.Corner(1), out _));
        Assert.True(book.TryGet(PhraseKeys.Fault(FaultKind.Lockup), out _));
    }

    [Fact]
    public void Ein_leerer_Satz_loescht_den_alten_Eintrag()
    {
        var book = new Phrasebook();
        book.Set(PhraseKeys.Corner(5), ["Später einlenken."]);

        Assert.Equal(0, book.Set(PhraseKeys.Corner(5), []));
        Assert.False(book.TryGet(PhraseKeys.Corner(5), out _));
        Assert.Equal(0, book.Count);
    }

    private static string Next(Phrasebook book)
    {
        Assert.True(book.TryGet(PhraseKeys.Corner(1), out string speech));
        return speech;
    }
}
