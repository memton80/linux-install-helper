using LinuxInstallHelper.Core.Network;

namespace LinuxInstallHelper.Core.Tests.Network;

public class WifiProfileXmlTests
{
    [Fact]
    public void Reads_the_name_and_the_clear_text_key()
    {
        var network = WifiProfileXml.Parse(Profile("Box-1234", "WPA2PSK", SharedKey("correct horse battery", isProtected: false)));

        Assert.Equal(new WifiNetwork("Box-1234", "correct horse battery", WifiSecurity.Password), network);
    }

    [Fact]
    public void An_encrypted_key_is_not_shown()
    {
        var network = WifiProfileXml.Parse(Profile("Box-1234", "WPA3SAE", SharedKey("01000000D08C9DDF0115D1118C7A00C04FC297EB", isProtected: true)));

        Assert.NotNull(network);
        Assert.Null(network.Password);
        Assert.Equal(WifiSecurity.Password, network.Security);
    }

    [Fact]
    public void Open_and_company_networks_have_no_password()
    {
        Assert.Equal(WifiSecurity.Open, WifiProfileXml.Parse(Profile("Gare", "open", string.Empty))?.Security);
        Assert.Equal(WifiSecurity.Open, WifiProfileXml.Parse(Profile("Cafe", "OWE", string.Empty))?.Security);
        Assert.Equal(WifiSecurity.Enterprise, WifiProfileXml.Parse(Profile("eduroam", "WPA2", string.Empty, oneX: true))?.Security);
        Assert.Equal(WifiSecurity.Enterprise, WifiProfileXml.Parse(Profile("Office", "WPA3ENT192", string.Empty))?.Security);
    }

    [Fact]
    public void The_ssid_wins_over_the_profile_name()
    {
        var xml = Profile("Box-1234", "WPA2PSK", SharedKey("secret", isProtected: false), profileName: "Maison");

        Assert.Equal("Box-1234", WifiProfileXml.Parse(xml)?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<LANProfile xmlns=\"http://www.microsoft.com/networking/LAN/profile/v1\" />")]
    public void Anything_else_is_ignored(string xml)
    {
        Assert.Null(WifiProfileXml.Parse(xml));
    }

    private static string SharedKey(string material, bool isProtected) => $"""
        <sharedKey>
          <keyType>passPhrase</keyType>
          <protected>{(isProtected ? "true" : "false")}</protected>
          <keyMaterial>{material}</keyMaterial>
        </sharedKey>
        """;

    private static string Profile(string ssid, string authentication, string sharedKey, bool oneX = false, string? profileName = null) => $"""
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>{profileName ?? ssid}</name>
          <SSIDConfig>
            <SSID>
              <hex>426F782D31323334</hex>
              <name>{ssid}</name>
            </SSID>
          </SSIDConfig>
          <connectionType>ESS</connectionType>
          <connectionMode>auto</connectionMode>
          <MSM>
            <security>
              <authEncryption>
                <authentication>{authentication}</authentication>
                <encryption>AES</encryption>
                <useOneX>{(oneX ? "true" : "false")}</useOneX>
              </authEncryption>
              {sharedKey}
            </security>
          </MSM>
        </WLANProfile>
        """;
}
