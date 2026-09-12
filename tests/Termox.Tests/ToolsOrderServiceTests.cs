using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ToolsOrderServiceTests
{
    [Fact]
    public void ApplySavedOrder_EmptySavedOrder_ReturnsCatalogOrderUnchanged()
    {
        var catalog = new[] { "A", "B", "C" };

        var result = ToolsOrderService.ApplySavedOrder(catalog, System.Array.Empty<string>());

        Assert.Equal(new[] { "A", "B", "C" }, result);
    }

    [Fact]
    public void ApplySavedOrder_FullSavedOrder_IsHonoredExactly()
    {
        var catalog = new[] { "A", "B", "C" };
        var saved = new[] { "C", "A", "B" };

        var result = ToolsOrderService.ApplySavedOrder(catalog, saved);

        Assert.Equal(new[] { "C", "A", "B" }, result);
    }

    [Fact]
    public void ApplySavedOrder_SavedIdNoLongerInCatalog_IsDroppedSilently()
    {
        var catalog = new[] { "A", "B" };
        var saved = new[] { "Removed", "B", "A" };

        var result = ToolsOrderService.ApplySavedOrder(catalog, saved);

        Assert.Equal(new[] { "B", "A" }, result);
    }

    [Fact]
    public void ApplySavedOrder_NewCatalogIdMissingFromSavedOrder_IsAppendedAtEnd()
    {
        var catalog = new[] { "A", "B", "NewTool" };
        var saved = new[] { "B", "A" };

        var result = ToolsOrderService.ApplySavedOrder(catalog, saved);

        Assert.Equal(new[] { "B", "A", "NewTool" }, result);
    }
}
