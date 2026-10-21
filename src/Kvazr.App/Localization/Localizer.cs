using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Kvazr.Core.Settings;

namespace Kvazr.App.Localization;

/// <summary>
/// Every string the interface says, in the language the user chose.
///
/// Three decisions worth writing down, two of which were made the other way first.
///
/// <b>English is the source.</b> The descriptors, the hints, the notes and the markup are
/// written in it, and everything else is a translation of it. It was Russian for a while,
/// on the argument that the author writes Russian — but the source language is what a
/// reader of the code sees, what a driver author writes, and what shows when a translation
/// is missing, and all three of those should be English here. So English needs no
/// catalogue at all and cannot go stale, and a key missing from another language shows the
/// English rather than a bare key or an empty control.
///
/// <b>The key is the sentence.</b> The catalogue started out keyed by invented dotted
/// paths, and that meant no file listed the strings: "translate everything" was
/// impossible, because each sentence had to be found in the code and given a key by hand,
/// one at a time. Keyed by the source text, the catalogue is the list.
///
/// <b>The switch is live.</b> Restarting to change language is the kind of thing that
/// makes a setting feel like a lie, and there is nothing here that needs a restart: the
/// catalogue is a dictionary and everything that draws text is already redrawn when
/// something it watches changes. So <see cref="Changed"/> is raised and the shell and the
/// pane both redraw.
/// </summary>
public sealed class Localizer
{
    /// <summary>
    /// The languages offered, in the order the settings screen lists them. English first
    /// because it is the source and the default; everything after it is a catalogue.
    /// </summary>
    public static IReadOnlyList<string> Cultures { get; } = ["en", "ru"];

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _catalogues;

    private string _culture = "en";

    public Localizer(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _catalogues = Load();
        Use(store.Current.Language);

        Current = this;
    }

    /// <summary>Catalogues, no settings. What <see cref="Current"/> holds until the real one exists.</summary>
    private Localizer() => _catalogues = Load();

    /// <summary>
    /// The catalogue in force, for the places that cannot be handed one.
    ///
    /// Ambient state, and deliberately: a capability view model is built by a factory from
    /// a reading and a session, in four different places including a design-time preview
    /// and the tests, and threading a catalogue through all of them to reach the twenty
    /// lines that actually print a word is worse than saying plainly that the interface
    /// language is a property of the process — which it is, exactly as the theme is.
    ///
    /// It starts as a catalogue with no settings behind it, so anything built before the
    /// application exists — a test, a preview — gets English, which is the source text and
    /// needs no catalogue at all.
    /// </summary>
    public static Localizer Current { get; private set; } = new();

    /// <summary>Raised after the language changes, on whichever thread changed it.</summary>
    public event EventHandler? Changed;

    /// <summary>The culture in use — "en" or "ru".</summary>
    public string Culture => _culture;

    /// <summary>
    /// One string. The key is a dotted path — "settings.theme", "rail.all_devices" — and
    /// the fallback is what to show when the catalogue has nothing, which for English is
    /// always and by design.
    /// </summary>
    public string this[string key, string fallback] =>
        _catalogues.TryGetValue(_culture, out FrozenDictionary<string, string>? catalogue)
        && catalogue.TryGetValue(key, out string? text)
        && text.Length > 0
            ? text
            : fallback;

    /// <summary>
    /// One string, keyed by the English text itself.
    ///
    /// This is what makes the catalogue a list rather than a scavenger hunt: every sentence
    /// in the product is a key, so translating is one pass over one file.
    ///
    /// It also reaches what the dotted keys could not. Capability names, their notes and
    /// the labels inside a choice are written in the drivers, a layer below this one that
    /// cannot see a catalogue at all; translated here, at the point where they are drawn,
    /// they need no change down there.
    ///
    /// The cost is the usual one for text keys: reword a sentence in the code and its
    /// translation stops matching. The failure is mild — the reworded English shows — and
    /// a test lists what the catalogue no longer covers.
    /// </summary>
    public string Say(string? english) =>
        english is { Length: > 0 } text ? this[text, text] : string.Empty;

    /// <summary>
    /// One sentence with something dropped into it: a count, a time, a device's name.
    ///
    /// The key is the whole sentence with the hole in it — "last seen — {0}" — and not
    /// the words around the hole. Where the value goes is part of the translation:
    /// languages put it in different places, and some need no word at all on one side of
    /// it. Translating the fragments separately produced sentences that were each
    /// correct and together meant nothing.
    ///
    /// Formatting is invariant on purpose; see the note in <see cref="Use"/> about why
    /// this catalogue moves words and never numbers or dates.
    /// </summary>
    public string Say(string english, params object?[] parts) =>
        string.Format(CultureInfo.InvariantCulture, Say(english), parts);

    /// <summary>
    /// A capability's own words, looked up by its id — "audio.volume" with "title" gives
    /// the key "cap.audio.volume.title".
    ///
    /// Keyed by id rather than by the driver's own words, so a wording change down there
    /// does not silently drop the translation — which is what a text-keyed catalogue does,
    /// and does quietly.
    /// </summary>
    public string Capability(string id, string part, string fallback) =>
        this[$"cap.{id}.{part}", Say(fallback)];

    /// <summary>
    /// Switches language. Anything unrecognised means English, including the empty string
    /// that a fresh settings file carries.
    /// </summary>
    public void Use(string? culture)
    {
        string wanted = Cultures.FirstOrDefault(
            c => culture?.StartsWith(c, StringComparison.OrdinalIgnoreCase) == true) ?? "en";

        if (wanted == _culture) return;

        _culture = wanted;

        // The formatting culture is deliberately left alone.
        //
        // The first version moved it too, so that dates and numbers would follow the
        // interface — and it crashed the application on start, because the build sets
        // InvariantGlobalization and CultureInfo.GetCultureInfo("en-GB") throws under it.
        // That is not a bug to route around: invariant is chosen for the whole product,
        // and under it there is exactly one way to format a number and a date whatever
        // this returns. So the catalogue translates words and nothing else, which is all
        // it was ever asked to do.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the catalogues out of the assembly. Embedded rather than loose files: a
    /// translation that can go missing from a published folder is a translation that will.
    /// </summary>
    private static FrozenDictionary<string, FrozenDictionary<string, string>> Load()
    {
        Assembly assembly = typeof(Localizer).Assembly;
        var built = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);

        foreach (string culture in Cultures)
        {
            string name = $"{typeof(Localizer).Namespace}.Strings.{culture}.json";

            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                built[culture] = FrozenDictionary<string, string>.Empty;
                continue;
            }

            Dictionary<string, string>? read =
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream);

            built[culture] = (read ?? []).ToFrozenDictionary(StringComparer.Ordinal);
        }

        return built.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every key the given culture defines. For the test that keeps them level.</summary>
    public IReadOnlyCollection<string> Keys(string culture) =>
        _catalogues.TryGetValue(culture, out FrozenDictionary<string, string>? catalogue)
            ? catalogue.Keys
            : [];
}
