using SubscriptionLibraries.Core.Utilities;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class NormalizationHelperTests
{
    [Theory]
    [InlineData("DOOM® Eternal (PC)", "doom eternal pc")]
    [InlineData("  Assassin’s   Creed™  ", "assassin s creed")]
    [InlineData("Pokémon: Example", "pokemon example")]
    [InlineData(null, "")]
    public void NormalizesTitlesConservatively(string? input, string expected)
    {
        Assert.Equal(expected, NormalizationHelper.NormalizeTitle(input));
    }

    [Fact]
    public void ProducesStableStoreSlug()
    {
        Assert.Equal("indiana-jones-and-the-great-circle", NormalizationHelper.CreateStoreSlug(
            "Indiana Jones and the Great Circle™"));
    }

    [Fact]
    public void UpgradesProtocolRelativeImageUrisToHttps()
    {
        var uri = NormalizationHelper.NormalizeHttpsUri("//images.example.test/cover.jpg");

        Assert.Equal("https://images.example.test/cover.jpg", uri?.AbsoluteUri);
    }
}
