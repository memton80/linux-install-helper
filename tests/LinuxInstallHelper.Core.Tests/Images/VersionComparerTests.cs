using LinuxInstallHelper.Core.Images;

namespace LinuxInstallHelper.Core.Tests.Images;

public class VersionComparerTests
{
    [Theory]
    [InlineData("13.10", "13.9")]
    [InlineData("26.04.1", "26.04")]
    [InlineData("2026.10.01", "2026.09.01")]
    [InlineData("r10", "r9")]
    [InlineData("b", "a")]
    public void First_is_greater(string greater, string smaller)
    {
        Assert.True(VersionComparer.Instance.Compare(greater, smaller) > 0);
        Assert.True(VersionComparer.Instance.Compare(smaller, greater) < 0);
    }

    [Fact]
    public void Leading_zeros_do_not_matter()
    {
        Assert.Equal(0, VersionComparer.Instance.Compare("1.02", "1.2"));
    }

    [Fact]
    public void Null_sorts_first()
    {
        Assert.True(VersionComparer.Instance.Compare(null, "1") < 0);
        Assert.Equal(0, VersionComparer.Instance.Compare(null, null));
    }
}
