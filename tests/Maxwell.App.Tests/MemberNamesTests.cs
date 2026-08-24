using Maxwell.App.Localization;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

using static Maxwell.App.Tests.Harness;

namespace Maxwell.App.Tests;

/// <summary>
/// The parts of a composite carry names too, and they were the last things left in
/// Russian after the interface was translated.
///
/// The reason is worth writing down, because nothing about it looked broken. A
/// capability's own title goes through the catalogue in <see cref="CapabilityViewModel"/>
/// and always did; but a host slot, a mouse button, a key cap and a lighting zone are
/// separate little view models built from <see cref="CapabilityMember"/>, and each of
/// them copied the driver's label straight into a property. So a page could be entirely
/// English above the fold and entirely Russian inside every list on it.
///
/// These are also the strings most likely to be missed by eye: they only appear on
/// devices with hosts, buttons or layers, which on this desk means the stand-ins.
/// </summary>
public sealed class MemberNamesTests
{
    private static CapabilityState Hosts() => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.LinkHosts,
            Domain = CapabilityDomain.Link,
            Primitive = CapabilityPrimitive.HostSlots,
            Access = CapabilityAccess.ReadWrite,
            Title = "Хосты",
            Members =
            [
                new CapabilityMember(0, "Хост 1 · донгл Lightspeed") { Hint = "Этот компьютер" },
                new CapabilityMember(1, "Хост 3 · свободен") { IsEmpty = true },
            ],
        },
        Value = CapabilityValue.Choice(0),
    };

    private static CapabilityState Buttons() => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.ButtonsMap,
            Domain = CapabilityDomain.Buttons,
            Primitive = CapabilityPrimitive.ButtonMap,
            Access = CapabilityAccess.ReadWrite,
            Title = "Назначение кнопок",
            Members = [new CapabilityMember(0, "Кнопка 4")],
            Choices = [new CapabilityChoice(0, "Назад"), new CapabilityChoice(1, "Средняя кнопка")],
        },
        Value = CapabilityValue.Vector([CapabilityValue.Choice(0)]),
    };

    /// <summary>Leaves the ambient catalogue in Russian, which is what every other test expects.</summary>
    private static void InEnglish(Action check)
    {
        var text = new Localizer(new Store());
        text.Use("en");

        try
        {
            check();
        }
        finally
        {
            text.Use("ru");
        }
    }

    [Fact]
    public void ASlotSaysItsNameAndWhatItHoldsInTheChosenLanguage()
    {
        InEnglish(() =>
        {
            var hosts = new HostSlotsCapabilityViewModel(Hosts(), null);

            Assert.Equal("Host 1 · Lightspeed dongle", hosts.Slots[0].Label);
            Assert.Equal("This computer", hosts.Slots[0].Hint);
            Assert.Equal("Host 3 · free", hosts.Slots[1].Label);
        });
    }

    [Fact]
    public void AButtonSaysBothItsOwnNameAndWhatItCanBeGiven()
    {
        InEnglish(() =>
        {
            var buttons = new ButtonMapCapabilityViewModel(Buttons(), null);

            Assert.Equal("Button 4", buttons.Buttons[0].Label);

            // The vocabulary of actions is the driver's too, and it is drawn in the same
            // list as the button's name — one translated and the other not would read as
            // a bug in the page rather than in the catalogue.
            Assert.Equal(["Back", "Middle button"], buttons.Buttons[0].Actions.Select(a => a.Label));
        });
    }

    /// <summary>
    /// The index is not translated and the word around it is, so the key has to be the
    /// whole sentence with the hole in it. A catalogue keyed on the bare word would put
    /// the number wherever Russian puts it, in every language.
    /// </summary>
    [Fact]
    public void ALayerTabKeepsItsNumberAndTranslatesTheWord()
    {
        InEnglish(() => Assert.Equal("Layer 2", new KeyLayer(2).Label));

        Assert.Equal("Слой 2", new KeyLayer(2).Label);
    }

    /// <summary>
    /// Names are looked up when they are drawn rather than copied when the row is built.
    /// Built once and kept for the life of the page, a row that copied its name would
    /// hold the language that happened to be in force at construction — so switching
    /// language would rename every page except the lists on it.
    /// </summary>
    [Fact]
    public void ARowBuiltInOneLanguageFollowsASwitchToTheOther()
    {
        var text = new Localizer(new Store());
        var hosts = new HostSlotsCapabilityViewModel(Hosts(), null);

        Assert.Equal("Хост 1 · донгл Lightspeed", hosts.Slots[0].Label);

        try
        {
            text.Use("en");
            Assert.Equal("Host 1 · Lightspeed dongle", hosts.Slots[0].Label);
        }
        finally
        {
            text.Use("ru");
        }
    }
}
