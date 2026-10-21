using Kvazr.App.Localization;
using Kvazr.Core.Settings;
using Xunit;

using static Kvazr.App.Tests.Harness;

namespace Kvazr.App.Tests;

/// <summary>
/// The catalogue, and the one way it fails silently.
///
/// MSBuild reads "Strings.ru.json" as a culture-tagged resource, the way it reads
/// "Form1.ru.resx", and routes it into a satellite assembly. The build stays green, the
/// file ships into a ru\ folder beside the executable, and the lookup finds nothing — so
/// the language switch appears to do nothing at all and there is no error anywhere to
/// explain it. That cost a debugging session; this is the test that will not let it
/// happen twice.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void ATranslationIsInsideTheAssembly()
    {
        var text = new Localizer(new Store());

        text.Use("ru");

        Assert.Equal("ru", text.Culture);
        Assert.NotEmpty(text.Keys("ru"));

        // A real string rather than a count, so a catalogue that loaded but is full of
        // blanks fails too.
        Assert.Equal("Параметры", text["rail.settings", "Settings"]);
    }

    [Fact]
    public void EnglishIsTheSourceAndNeedsNoCatalogue()
    {
        var text = new Localizer(new Store());

        // Nothing is stored for English, by design: the strings are already in the code,
        // and a second copy of every sentence is a second thing to keep true.
        Assert.Equal("en", text.Culture);
        Assert.Empty(text.Keys("en"));
        Assert.Equal("Settings", text["rail.settings", "Settings"]);
    }

    [Fact]
    public void AKeyTheCatalogueDoesNotCarryShowsTheSourceRatherThanNothing()
    {
        var text = new Localizer(new Store());
        text.Use("ru");

        // An empty control or a bare "settings.invented" would both be worse than the
        // sentence the code already has.
        Assert.Equal("Something in English", text["settings.invented", "Something in English"]);
    }

    [Fact]
    public void TheSwitchAnnouncesItselfAndOnlyWhenItMoves()
    {
        var text = new Localizer(new Store());
        int announced = 0;
        text.Changed += (_, _) => announced++;

        text.Use("ru");
        text.Use("ru");
        text.Use("en");

        // Twice, not three times: setting the language to what it already is redraws
        // every open page for nothing.
        Assert.Equal(2, announced);
    }

    [Fact]
    public void AnUnknownLanguageIsEnglish()
    {
        var store = new Store();
        store.Current.Language = "de-DE";

        var text = new Localizer(store);

        Assert.Equal("en", text.Culture);
    }

    /// <summary>
    /// A capability can be translated by its id as well as by its words, so a translator
    /// can separate two capabilities that happen to share a name in English without the
    /// drivers knowing anything about it.
    /// </summary>
    [Fact]
    public void ACapabilityIsLookedUpByItsIdAndThenByItsWords()
    {
        var text = new Localizer(new Store());
        text.Use("ru");

        // No entry for this id, which is the ordinary case — so it falls through to the
        // source text, and the source text is itself a key. That is the whole point of
        // keying by it: a driver's own words are translated where they are drawn, without
        // the driver knowing a catalogue exists.
        Assert.Equal("Эквалайзер", text.Capability("audio.eq_graphic", "title", "Equaliser"));
        Assert.Equal("Эквалайзер", text.Capability("audio.eq_graphic", "invented", "Equaliser"));

        // Nothing knows this sentence, so it shows as written.
        Assert.Equal("An invention", text.Capability("audio.invented", "title", "An invention"));
    }
}
