namespace LinuxInstallHelper.Core.Readiness;

/// <summary>
/// The keys that open the boot menu and the UEFI/BIOS settings of a computer, right after it is switched on. Keys are
/// tokens (<c>F12</c>, <c>Esc</c>, <c>Del</c>, <c>VolumeDown</c>…) that the interface names in the user's language.
/// </summary>
/// <param name="Vendor">The maker, as shown to the user ("Dell", "Lenovo ThinkPad"…).</param>
/// <param name="BootMenu">Opens the menu that lists the devices to start from.</param>
/// <param name="Setup">Opens the UEFI/BIOS settings, null when there are none to open.</param>
/// <param name="Note">Token of an extra hint (<c>AcerF12</c>, <c>Hold</c>), or null.</param>
public sealed record FirmwareKeys(string Vendor, string BootMenu, string? Setup, string? Note = null)
{
    private static readonly char[] WordSeparators = [' ', '.', ',', '-', '(', ')'];

    // Values that motherboard makers leave in place of the computer's maker.
    private static readonly string[] PlaceholderMakers =
        ["System manufacturer", "System Product Name", "To Be Filled By O.E.M.", "Default string", "OEM", "Not Applicable"];

    // Computer makers, matched on the maker of the computer (laptops and computers sold assembled).
    private static readonly Rule[] ComputerRules =
    [
        new(["Dell"], "Dell", "F12", "F2"),
        new(["Alienware"], "Alienware", "F12", "F2"),
        new(["HP", "Hewlett-Packard", "Hewlett Packard"], "HP", "F9", "F10"),
        new(["Acer", "Packard Bell", "Gateway"], "Acer", "F12", "F2", "AcerF12"),
        new(["ASUSTeK", "ASUS"], "ASUS", "Esc", "F2"),
        new(["Micro-Star", "MSI"], "MSI", "F11", "Del"),
        new(["GIGABYTE", "AORUS"], "Gigabyte", "F12", "F2"),
        new(["Toshiba", "Dynabook"], "Toshiba / Dynabook", "F12", "F2"),
        new(["Samsung"], "Samsung", "F10", "F2"),
        new(["Fujitsu"], "Fujitsu", "F12", "F2"),
        new(["Huawei", "Honor"], "Huawei", "F12", "F2"),
        new(["Framework"], "Framework", "F12", "F2"),
        new(["Apple"], "Apple", "Option", null, "Hold"),
    ];

    // Motherboard makers, for computers assembled from parts.
    private static readonly Rule[] BoardRules =
    [
        new(["ASUSTeK", "ASUS"], "ASUS", "F8", "Del"),
        new(["Micro-Star", "MSI"], "MSI", "F11", "Del"),
        new(["GIGABYTE", "AORUS"], "Gigabyte", "F12", "Del"),
        new(["ASRock"], "ASRock", "F11", "F2"),
        new(["Intel"], "Intel", "F10", "F2"),
    ];

    /// <summary>The keys of this computer, or null when its maker is unknown.</summary>
    /// <param name="manufacturer">Maker of the computer (<c>Win32_ComputerSystem.Manufacturer</c>).</param>
    /// <param name="model">Model (<c>Win32_ComputerSystem.Model</c>).</param>
    /// <param name="family">Product line (<c>Win32_ComputerSystem.SystemFamily</c>), used for Lenovo.</param>
    /// <param name="boardManufacturer">Maker of the motherboard (<c>Win32_BaseBoard.Manufacturer</c>).</param>
    public static FirmwareKeys? For(string? manufacturer, string? model, string? family, string? boardManufacturer)
    {
        var maker = IsPlaceholder(manufacturer) ? null : manufacturer!.Trim();
        if (maker is not null)
        {
            if (Matches(maker, "Lenovo"))
            {
                // ThinkPad, ThinkCentre and ThinkStation open their settings with F1, the other Lenovo computers with F2.
                var think = $"{family} {model}".Contains("Think", StringComparison.OrdinalIgnoreCase);
                return new FirmwareKeys(think ? "Lenovo ThinkPad" : "Lenovo", "F12", think ? "F1" : "F2");
            }

            if (Matches(maker, "Microsoft") && $"{family} {model}".Contains("Surface", StringComparison.OrdinalIgnoreCase))
            {
                return new FirmwareKeys("Microsoft Surface", "VolumeDown", "VolumeUp", "Hold");
            }

            if (ComputerRules.FirstOrDefault(r => r.Matches(maker)) is { } computer)
            {
                return computer.Keys;
            }
        }

        var board = IsPlaceholder(boardManufacturer) ? null : boardManufacturer!.Trim();
        return board is null ? null : BoardRules.FirstOrDefault(r => r.Matches(board))?.Keys;
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || PlaceholderMakers.Any(p => value.Trim().Equals(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Short names (HP, MSI) must be whole words: "HP" is not found in "Hewlett" nor "MSI" in "Mission".</summary>
    private static bool Matches(string value, string name)
    {
        if (name.Length > 4)
        {
            return value.Contains(name, StringComparison.OrdinalIgnoreCase);
        }

        return value.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Rule(string[] Names, string Vendor, string BootMenu, string? Setup, string? Note = null)
    {
        public FirmwareKeys Keys => new(Vendor, BootMenu, Setup, Note);

        public bool Matches(string value) => Names.Any(name => FirmwareKeys.Matches(value, name));
    }
}
