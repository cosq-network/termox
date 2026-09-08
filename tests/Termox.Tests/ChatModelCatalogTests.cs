using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatModelCatalogTests
{
    [Fact]
    public void KnownModelIsFoundById()
    {
        var model = ChatModelCatalog.Find("gpt-4o-mini");

        Assert.NotNull(model);
        Assert.Equal("OpenAI", model!.Provider);
        Assert.True(model.ContextWindowTokens > 0);
    }

    [Fact]
    public void UnknownModelIsNotFound()
    {
        Assert.Null(ChatModelCatalog.Find("some-made-up-model-id"));
        Assert.False(ChatModelCatalog.IsKnownModel("some-made-up-model-id"));
    }

    [Fact]
    public void EmptyOrNullModelIdIsNotFound()
    {
        Assert.Null(ChatModelCatalog.Find(""));
        Assert.Null(ChatModelCatalog.Find(null));
    }

    [Fact]
    public void CustomSentinelIsNotItselfAKnownModel()
    {
        Assert.False(ChatModelCatalog.IsKnownModel(ChatModelCatalog.CustomModelSentinel));
    }
}
