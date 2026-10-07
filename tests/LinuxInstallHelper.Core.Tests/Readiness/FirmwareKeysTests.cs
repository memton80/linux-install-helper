using LinuxInstallHelper.Core.Readiness;

namespace LinuxInstallHelper.Core.Tests.Readiness;

public class FirmwareKeysTests
{
    [Theory]
    [InlineData("Dell Inc.", "Latitude 5420", null, "Dell", "F12", "F2")]
    [InlineData("HP", "HP Laptop 15s-fq2xxx", null, "HP", "F9", "F10")]
    [InlineData("Hewlett-Packard", "HP Compaq 8200 Elite", null, "HP", "F9", "F10")]
    [InlineData("Acer", "Aspire A515-56", null, "Acer", "F12", "F2")]
    [InlineData("ASUSTeK COMPUTER INC.", "VivoBook_ASUSLaptop X515EA", null, "ASUS", "Esc", "F2")]
    [InlineData("Micro-Star International Co., Ltd.", "GF63 Thin 11UC", null, "MSI", "F11", "Del")]
    [InlineData("LENOVO", "20XW0055FR", "ThinkPad X1 Carbon Gen 9", "Lenovo ThinkPad", "F12", "F1")]
    [InlineData("LENOVO", "82KU", "IdeaPad 3 15ALC6", "Lenovo", "F12", "F2")]
    [InlineData("Microsoft Corporation", "Surface Laptop 4", "Surface", "Microsoft Surface", "VolumeDown", "VolumeUp")]
    [InlineData("Apple Inc.", "MacBookPro15,1", null, "Apple", "Option", null)]
    public void Finds_the_keys_of_the_maker(string manufacturer, string model, string? family, string vendor, string bootMenu, string? setup)
    {
        var keys = FirmwareKeys.For(manufacturer, model, family, null);

        Assert.NotNull(keys);
        Assert.Equal(vendor, keys.Vendor);
        Assert.Equal(bootMenu, keys.BootMenu);
        Assert.Equal(setup, keys.Setup);
    }

    [Fact]
    public void An_assembled_computer_uses_the_keys_of_its_motherboard()
    {
        var keys = FirmwareKeys.For("System manufacturer", "System Product Name", "To be filled by O.E.M.", "ASUSTeK COMPUTER INC.");

        Assert.Equal(new FirmwareKeys("ASUS", "F8", "Del"), keys);
        Assert.Equal("MSI", FirmwareKeys.For("To Be Filled By O.E.M.", null, null, "Micro-Star International Co., Ltd.")?.Vendor);
    }

    [Fact]
    public void Virtual_machines_and_unknown_makers_have_no_keys()
    {
        Assert.Null(FirmwareKeys.For("Microsoft Corporation", "Virtual Machine", "Virtual Machine", "Microsoft Corporation"));
        Assert.Null(FirmwareKeys.For("innotek GmbH", "VirtualBox", null, "Oracle Corporation"));
        Assert.Null(FirmwareKeys.For(null, null, null, null));
    }

    [Fact]
    public void Short_names_must_be_whole_words()
    {
        Assert.Null(FirmwareKeys.For("Shpock Systems", null, null, null));
        Assert.Equal("HP", FirmwareKeys.For("HP Inc.", null, null, null)?.Vendor);
    }
}
