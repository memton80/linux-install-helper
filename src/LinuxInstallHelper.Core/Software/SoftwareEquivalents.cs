using System.Text.RegularExpressions;

namespace LinuxInstallHelper.Core.Software;

/// <summary>What happens to a Windows program on Linux.</summary>
public enum SoftwareVerdict
{
    /// <summary>The same program exists for Linux.</summary>
    Native,

    /// <summary>It does not, but free programs do the same job.</summary>
    Alternative,

    /// <summary>Use its website (or web version) in the browser.</summary>
    Web,

    /// <summary>Linux does not need it (antivirus, cleaners, drivers panels…).</summary>
    NotNeeded,

    /// <summary>Only on Windows, with no real equivalent: keep Windows next to Linux for it.</summary>
    WindowsOnly,

    /// <summary>Not in the list: to look up.</summary>
    Unknown,
}

/// <param name="Verdict">What happens to the program on Linux.</param>
/// <param name="Suggestions">Programs or websites to use on Linux (product names, not translated), when there are.</param>
public sealed record SoftwareAdvice(SoftwareVerdict Verdict, string? Suggestions = null);

/// <summary>
/// The Linux side of well-known Windows programs. Only programs whose situation is clear are listed: the others are
/// <see cref="SoftwareVerdict.Unknown"/> and the user looks them up.
/// </summary>
public static class SoftwareEquivalents
{
    // Patterns are regular expressions matched as whole words, first match wins: specific names come before general ones.
    private static readonly Rule[] Rules =
    [
        // Browsers, mail, messaging
        new("Google Chrome|Mozilla Firefox|Firefox|Brave|Vivaldi|Opera( GX)?|Tor Browser|Microsoft Edge", SoftwareVerdict.Native),
        new("Thunderbird", SoftwareVerdict.Native),
        new("Microsoft Outlook|Mailbird|eM Client", SoftwareVerdict.Alternative, "Thunderbird, Evolution"),
        new("Microsoft Teams|Teams Machine-Wide Installer", SoftwareVerdict.Web, "teams.microsoft.com"),
        new("WhatsApp", SoftwareVerdict.Web, "web.whatsapp.com"),
        new("Messenger", SoftwareVerdict.Web, "messenger.com"),
        new("Zoom|Slack|Discord|Telegram|Signal|Element|Webex", SoftwareVerdict.Native),

        // Office
        new("Microsoft OneNote", SoftwareVerdict.Web, "OneNote (onenote.com), Joplin"),
        new("Microsoft (Office|365)|Office 16 Click-to-Run|WPS Office", SoftwareVerdict.Alternative, "LibreOffice, OnlyOffice, Microsoft 365 (office.com)"),
        new("LibreOffice|OnlyOffice|ONLYOFFICE", SoftwareVerdict.Native),
        new("Adobe Acrobat|Acrobat Reader|Foxit PDF Reader|Foxit Reader|PDF24", SoftwareVerdict.Alternative, "Okular, Papers / Evince, PDF Arranger"),
        new("Obsidian|Joplin|Logseq|Zotero|Calibre|Anki|Xmind", SoftwareVerdict.Native),
        new("Notion|Evernote|Canva|Figma", SoftwareVerdict.Web),

        // Cloud and passwords
        new("Dropbox|Nextcloud|MEGAsync|pCloud", SoftwareVerdict.Native),
        new("Microsoft OneDrive|OneDrive", SoftwareVerdict.Alternative, "onedrive.live.com, OneDriver, rclone"),
        new("Google Drive", SoftwareVerdict.Alternative, "drive.google.com, GNOME Online Accounts, Insync"),
        new("iCloud", SoftwareVerdict.Web, "icloud.com"),
        new("KeePassXC|Bitwarden|1Password|Proton Pass", SoftwareVerdict.Native),
        new("KeePass", SoftwareVerdict.Alternative, "KeePassXC"),
        new("Dashlane|LastPass", SoftwareVerdict.Web),
        new("NordVPN|ExpressVPN|Proton ?VPN|Surfshark|Mullvad VPN|Windscribe", SoftwareVerdict.Native),

        // Pictures, video, sound
        new("Adobe Photoshop Lightroom|Lightroom", SoftwareVerdict.Alternative, "darktable, RawTherapee"),
        new("Adobe Photoshop|Photoshop Elements|Affinity Photo", SoftwareVerdict.Alternative, "GIMP, Krita, Photopea (photopea.com)"),
        new("Adobe Illustrator|Affinity Designer|CorelDRAW", SoftwareVerdict.Alternative, "Inkscape"),
        new("Adobe InDesign|Affinity Publisher", SoftwareVerdict.Alternative, "Scribus"),
        new("Adobe Premiere|Premiere Elements|Vegas Pro|CyberLink PowerDirector|Filmora|CapCut|Clipchamp", SoftwareVerdict.Alternative, "Kdenlive, Shotcut, DaVinci Resolve"),
        new("Adobe After Effects", SoftwareVerdict.Alternative, "Natron, Blender"),
        new("Adobe Creative Cloud", SoftwareVerdict.Alternative, "GIMP, Inkscape, Kdenlive, Scribus"),
        new("paint\\.net", SoftwareVerdict.Alternative, "Pinta"),
        new("IrfanView|FastStone Image Viewer|ACDSee", SoftwareVerdict.Alternative, "gThumb, nomacs"),
        new("GIMP|Inkscape|Krita|Blender|darktable|RawTherapee|XnView MP|DaVinci Resolve|Kdenlive|Shotcut|OpenShot|Natron|Scribus", SoftwareVerdict.Native),
        new("OBS Studio|HandBrake|MKVToolNix|Audacity|MuseScore|Reaper|Bitwig Studio|Ardour|LMMS", SoftwareVerdict.Native),
        new("VLC media player|VLC|Kodi|Plex( Media Server)?|Jellyfin( Media Player| Server)?|Spotify|Strawberry", SoftwareVerdict.Native),
        new("MPC-HC|MPC-BE|PotPlayer|Media Player Classic|KMPlayer|GOM Player", SoftwareVerdict.Alternative, "VLC, mpv, Celluloid"),
        new("iTunes|MusicBee|foobar2000|AIMP|Winamp", SoftwareVerdict.Alternative, "Rhythmbox, Strawberry, Elisa"),
        new("K-Lite Codec Pack|Codec Pack", SoftwareVerdict.NotNeeded),
        new("ShareX|Greenshot|Snagit|Lightshot", SoftwareVerdict.Alternative, "Flameshot, Spectacle"),
        new("Camtasia|Bandicam|Fraps", SoftwareVerdict.Alternative, "OBS Studio, Kooha"),
        new("FL Studio|Ableton Live|Cubase|Pro Tools|Native Instruments|Steinberg", SoftwareVerdict.WindowsOnly, "Bitwig Studio, Ardour, Reaper, LMMS"),
        new("Netflix|Disney\\+|Prime Video|Deezer|Apple Music|Apple TV", SoftwareVerdict.Web),
        new("Kindle", SoftwareVerdict.Alternative, "Calibre, read.amazon.com"),

        // Games
        new("Steam|Minecraft Launcher|Minecraft|itch|Heroic Games Launcher|Lutris|Prism Launcher", SoftwareVerdict.Native),
        new("Riot Vanguard|Riot Client|League of Legends|VALORANT|FACEIT|Fortnite|Roblox|Call of Duty|Battlefield|PUBG|Apex Legends|Rainbow Six", SoftwareVerdict.WindowsOnly),
        new("Epic Games Launcher|GOG Galaxy|Amazon Games", SoftwareVerdict.Alternative, "Heroic Games Launcher"),
        new("EA app|Ubisoft Connect|Uplay|Battle\\.net|Rockstar Games Launcher", SoftwareVerdict.Alternative, "Lutris, Bottles, Steam (Proton)"),
        new("Xbox|Game Bar|Microsoft Gaming Services", SoftwareVerdict.Web, "xbox.com/play"),

        // Development
        new("Microsoft Visual Studio Code|Visual Studio Code|VSCodium|Sublime Text|Cursor", SoftwareVerdict.Native),
        new("Microsoft Visual Studio|Visual Studio (Community|Professional|Enterprise|Build Tools)", SoftwareVerdict.Alternative, "Visual Studio Code, JetBrains Rider"),
        new("IntelliJ IDEA|PyCharm|WebStorm|Rider|CLion|GoLand|PhpStorm|DataGrip|RubyMine|Android Studio|JetBrains Toolbox|Eclipse|NetBeans", SoftwareVerdict.Native),
        new("Git|Python|Node\\.js|Docker Desktop|Postman|Insomnia|Wireshark|Unity Hub|Unity|Unreal Engine|Godot|Arduino IDE|MATLAB|RStudio|R for Windows|GeoGebra", SoftwareVerdict.Native),
        new("Java|Eclipse Temurin|OpenJDK|Oracle JDK|Amazon Corretto", SoftwareVerdict.Native),
        new("Notepad\\+\\+", SoftwareVerdict.Alternative, "Notepad Next, Kate, Text Editor"),
        new("GitHub Desktop|TortoiseGit|SourceTree", SoftwareVerdict.Alternative, "GitKraken, gitg, Git (terminal)"),
        new("PuTTY|FileZilla|WinSCP", SoftwareVerdict.Native),
        new("MobaXterm|Termius", SoftwareVerdict.Alternative, "Terminal (ssh), Remmina"),
        new("Windows Subsystem for Linux|WSL", SoftwareVerdict.NotNeeded),
        new("VirtualBox|VMware Workstation|VMware Player", SoftwareVerdict.Native),

        // Remote access
        new("TeamViewer|AnyDesk|RustDesk|Parsec|Remmina", SoftwareVerdict.Native),

        // Files and system tools
        new("7-Zip|WinRAR|WinZip|Bandizip|PeaZip", SoftwareVerdict.Alternative, "PeaZip, File Roller / Ark"),
        new("qBittorrent|Transmission|Deluge", SoftwareVerdict.Native),
        new("uTorrent|µTorrent|BitTorrent", SoftwareVerdict.Alternative, "qBittorrent, Transmission"),
        new("Everything", SoftwareVerdict.Alternative, "FSearch"),
        new("WinDirStat|TreeSize|WizTree|SpaceSniffer", SoftwareVerdict.Alternative, "Baobab, Filelight"),
        new("CPU-Z|HWiNFO|HWMonitor|Speccy|GPU-Z", SoftwareVerdict.Alternative, "CPU-X, Hardinfo2, Mission Center"),
        new("CrystalDiskInfo", SoftwareVerdict.Alternative, "GNOME Disks, GSmartControl"),
        new("CrystalDiskMark", SoftwareVerdict.Alternative, "KDiskMark"),
        new("Recuva|EaseUS Data Recovery", SoftwareVerdict.Alternative, "PhotoRec, TestDisk"),
        new("EaseUS Partition Master|MiniTool Partition Wizard|AOMEI Partition Assistant", SoftwareVerdict.Alternative, "GParted, KDE Partition Manager"),
        new("Macrium Reflect|Acronis True Image|AOMEI Backupper|EaseUS Todo Backup|Veeam Agent", SoftwareVerdict.Alternative, "Timeshift, Déjà Dup, Rescuezilla"),
        new("balenaEtcher|Etcher|Ventoy", SoftwareVerdict.Native),
        new("Rufus", SoftwareVerdict.Alternative, "balenaEtcher, Impression"),
        new("AutoHotkey", SoftwareVerdict.Alternative, "AutoKey, Input Remapper"),
        new("Rainmeter", SoftwareVerdict.Alternative, "Conky"),
        new("CCleaner|Glary Utilities|Wise Care 365|Advanced SystemCare|IObit|Revo Uninstaller|Driver Booster|Defraggler", SoftwareVerdict.NotNeeded, "BleachBit"),
        new("Avast|AVG|Norton|McAfee|Kaspersky|Bitdefender|ESET|Malwarebytes|Avira|Panda|Total ?AV|Webroot|Sophos", SoftwareVerdict.NotNeeded),
        new("f\\.lux|TranslucentTB|StartAllBack|Start11|Open-Shell|Classic Shell|PowerToys|Windhawk", SoftwareVerdict.NotNeeded),

        // Hardware
        new("Logitech G HUB|Logitech Gaming Software", SoftwareVerdict.Alternative, "Piper, Solaar"),
        new("Logi Options|Logitech Options", SoftwareVerdict.Alternative, "Solaar"),
        new("Razer Synapse|Razer Cortex", SoftwareVerdict.Alternative, "OpenRazer, Polychromatic"),
        new("Corsair iCUE|iCUE|SteelSeries GG|SignalRGB|Armoury Crate|MSI Center|Mystic Light", SoftwareVerdict.Alternative, "OpenRGB, ckb-next"),
        new("MSI Afterburner|EVGA Precision", SoftwareVerdict.Alternative, "MangoHud, CoreCtrl, LACT"),
        new("NVIDIA GeForce Experience|NVIDIA App|NVIDIA Control Panel|AMD Adrenalin|Intel Driver & Support Assistant|Intel Graphics Command Center", SoftwareVerdict.NotNeeded),
        new("Stream Deck", SoftwareVerdict.Alternative, "StreamController"),
        new("HP Smart|HP Support Assistant|HP Easy Start", SoftwareVerdict.Alternative, "HPLIP"),
        new("Garmin Express|iCloud for Windows|Samsung Smart Switch|Apple Devices", SoftwareVerdict.WindowsOnly),

        // Technical drawing
        new("AutoCAD|SolidWorks|SOLIDWORKS|Autodesk Fusion|Fusion 360|Revit|ArchiCAD|CATIA|Inventor", SoftwareVerdict.WindowsOnly, "FreeCAD, LibreCAD, Onshape (onshape.com)"),
        new("SketchUp", SoftwareVerdict.Web, "app.sketchup.com"),
        new("FreeCAD|LibreCAD|KiCad|OpenSCAD|Cura|UltiMaker Cura|PrusaSlicer|OrcaSlicer", SoftwareVerdict.Native),
    ];

    /// <summary>The advice for the program named <paramref name="name"/>.</summary>
    public static SoftwareAdvice For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Rules.FirstOrDefault(r => r.Pattern.IsMatch(name))?.Advice ?? new SoftwareAdvice(SoftwareVerdict.Unknown);
    }

    private sealed class Rule(string pattern, SoftwareVerdict verdict, string? suggestions = null)
    {
        // Whole words: "Git" is not found in "GitHub" nor "Rider" in "Outrider".
        public Regex Pattern { get; } = new($@"(?<![\w.+-])(?:{pattern})(?![\w+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public SoftwareAdvice Advice { get; } = new(verdict, suggestions);
    }
}
