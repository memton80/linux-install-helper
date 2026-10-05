using LinuxInstallHelper.Core.Backup;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Backup;

public sealed class PersonalFoldersTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Empty_and_missing_folders_are_left_out()
    {
        var empty = Folder(PersonalFolderKind.Documents);
        Directory.CreateDirectory(Path.Combine(empty.Path, "Empty subfolder"));
        var missing = new PersonalFolder(PersonalFolderKind.Music, _temp.File("missing"));

        Assert.Empty(PersonalFolders.WithFiles([empty, missing]));
    }

    [Fact]
    public void Folders_with_files_are_found_in_order()
    {
        var documents = Folder(PersonalFolderKind.Documents);
        var pictures = Folder(PersonalFolderKind.Pictures);
        var videos = Folder(PersonalFolderKind.Videos);
        AddFile(documents, "Invoices", "2026", "march.pdf");
        AddFile(videos, "holidays.mp4");

        var found = PersonalFolders.WithFiles([documents, pictures, videos]);

        Assert.Equal([PersonalFolderKind.Documents, PersonalFolderKind.Videos], found.Select(folder => folder.Kind));
    }

    [Fact]
    public void Shortcuts_and_hidden_or_system_files_do_not_count()
    {
        var desktop = Folder(PersonalFolderKind.Desktop);
        AddFile(desktop, "Microsoft Edge.lnk");
        AddFile(desktop, "Website.URL");
        AddFile(desktop, "desktop.ini");
        var hidden = AddFile(desktop, ".hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Empty(PersonalFolders.WithFiles([desktop]));
    }

    [Fact]
    public void Ignored_files_do_not_count()
    {
        var downloads = Folder(PersonalFolderKind.Downloads);
        var image = AddFile(downloads, "ubuntu-26.04.1-desktop-amd64.iso");

        Assert.Empty(PersonalFolders.WithFiles([downloads], [image]));

        AddFile(downloads, "letter.odt");
        Assert.Single(PersonalFolders.WithFiles([downloads], [image]));
    }

    [Fact]
    public void A_folder_listed_twice_is_returned_once()
    {
        var documents = Folder(PersonalFolderKind.Documents);
        AddFile(documents, "notes.txt");

        var found = PersonalFolders.WithFiles([documents, documents with { Kind = PersonalFolderKind.Pictures }]);

        Assert.Equal(PersonalFolderKind.Documents, Assert.Single(found).Kind);
    }

    private PersonalFolder Folder(PersonalFolderKind kind)
    {
        var path = _temp.File(kind.ToString());
        Directory.CreateDirectory(path);
        return new PersonalFolder(kind, path);
    }

    private static string AddFile(PersonalFolder folder, params string[] parts)
    {
        var path = Path.Combine([folder.Path, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        return path;
    }
}
