using SubscriptionLibraries.Core.Providers.GamePass;
using Xunit;

namespace SubscriptionLibraries.Tests;

public sealed class GamePassProductPagesTests
{
    [Fact]
    public void UsesDocumentedProductPageSchemesForMicrosoftProductId()
    {
        var pages = GamePassProductPages.FromProductId(" 9nv0zmv5gb11 ");

        Assert.NotNull(pages);
        Assert.Equal("msxbox://game/?productId=9NV0ZMV5GB11", pages.XboxApp.AbsoluteUri);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9NV0ZMV5GB11",
            pages.MicrosoftStore.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9NV0ZMV5GB1")]
    [InlineData("9NV0ZMV5GB11&")]
    [InlineData("9NV0ZMV5GB1/")]
    [InlineData("9NV0ZMV5GB1\u00E9")]
    public void RejectsIdsThatAreNotMicrosoftProductIds(string? productId)
    {
        Assert.Null(GamePassProductPages.FromProductId(productId));
    }
}
