using Maxwell.App.Localization;
using Maxwell.Core.Settings;
using Xunit;

using static Maxwell.App.Tests.Harness;

namespace Maxwell.App.Tests;

/// <summary>
/// The catalogue, and the one way it fails silently.
///
/// MSBuild reads "Strings.en.json" as a culture-tagged resource, the way it reads
/// "Form1.en.resx", and routes it into a satellite assembly. The build stays green, the
/// file ships into an en\ folder beside the executable, and the lookup finds nothing — so
/// the language switch appears to do nothing at all and there is no error anywhere to
/// explain it. That cost a debugging session; this is the test that will not let it
/// happen twice.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void TheEnglishCatalogueIsInsideTheAssembly()
    {
        var text = new Localizer(new Store());

        text.Use("en");

        Assert.Equal("en", text.Culture);
        Assert.NotEmpty(text.Keys("en"));

        // A real string rather than a count, so a catalogue that loaded but is full of
        // blanks fails too.
        Assert.Equal("Settings", text["page.settings", "Параметры"]);
    }

    [Fact]
    public void RussianIsTheSourceAndNeedsNoCatalogue()
    {
        var text = new Localizer(new Store());

        // Nothing is stored for Russian, by design: the strings are already in the code,
        // and a second copy of every sentence is a second thing to keep true.
        Assert.Equal("ru", text.Culture);
        Assert.Empty(text.Keys("ru"));
        Assert.Equal("Параметры", text["page.settings", "Параметры"]);
    }

    [Fact]
    public void AKeyTheCatalogueDoesNotCarryShowsTheRussianRatherThanNothing()
    {
        var text = new Localizer(new Store());
        text.Use("en");

        // An empty control or a bare "settings.invented" would both be worse than the
        // sentence the code already has.
        Assert.Equal("Что-то по-русски", text["settings.invented", "Что-то по-русски"]);
    }

    [Fact]
    public void TheSwitchAnnouncesItselfAndOnlyWhenItMoves()
    {
        var text = new Localizer(new Store());
        int announced = 0;
        text.Changed += (_, _) => announced++;

        text.Use("en");
        text.Use("en");
        text.Use("ru");

        // Twice, not three times: setting the language to what it already is redraws
        // every open page for nothing.
        Assert.Equal(2, announced);
    }

    [Fact]
    public void AnUnknownLanguageIsRussian()
    {
        var store = new Store();
        store.Current.Language = "de-DE";

        var text = new Localizer(store);

        Assert.Equal("ru", text.Culture);
    }

    /// <summary>
    /// The capability lookup is keyed by id, so a wording change in the driver cannot
    /// silently drop the translation the way a text-keyed catalogue would.
    /// </summary>
    [Fact]
    public void ACapabilityIsLookedUpByItsId()
    {
        var text = new Localizer(new Store());
        text.Use("en");

        Assert.Equal("Equaliser", text.Capability("audio.eq_graphic", "title", "Эквалайзер"));
        Assert.Equal("Эквалайзер", text.Capability("audio.eq_graphic", "invented", "Эквалайзер"));
    }
}
