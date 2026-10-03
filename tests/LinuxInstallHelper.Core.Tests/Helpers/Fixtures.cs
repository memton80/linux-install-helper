namespace LinuxInstallHelper.Core.Tests.Helpers;

public static class Fixtures
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string PathOf(string name) => Path.Combine(Root, name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static string Fingerprint(string name) =>
        File.ReadAllLines(PathOf("fingerprints.txt"))
            .Select(l => l.Split('='))
            .Single(p => p[0] == name)[1];

    public const string UbuntuFingerprint = "843938DF228D22F7B3742BC0D94AA3F0EFE21092";
}

/// <summary>Temporary folder deleted at the end of a test.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lih-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
