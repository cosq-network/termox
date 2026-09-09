using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class OpenAiChatClientErrorTests
{
    [Fact]
    public void ExtractsMessageAndRemedyHintFromOpenRouterStyleBody()
    {
        var body = """
        {"error":{"message":"This request requires more credits, or fewer max_tokens. You requested up to 16384 tokens, but can only afford 3324.","code":402,"metadata":{"limit_source":"openrouter_credits","remedy_hint":"Add credits at https://openrouter.ai/settings/credits, or lower max_tokens / prompt size to fit your remaining balance."}},"user_id":"user_abc123"}
        """;

        var friendly = OpenAiChatClient.BuildFriendlyErrorMessage(402, "Payment Required", body);

        Assert.StartsWith("Payment required.", friendly);
        Assert.Contains("This request requires more credits", friendly);
        Assert.Contains("Add credits at https://openrouter.ai/settings/credits", friendly);
        Assert.DoesNotContain("user_id", friendly);
        Assert.DoesNotContain("limit_source", friendly);
    }

    [Fact]
    public void ExtractsMessageWithoutRemedyHintFromOpenAiStyleBody()
    {
        var body = """{"error":{"message":"Invalid API key provided.","type":"invalid_request_error"}}""";

        var friendly = OpenAiChatClient.BuildFriendlyErrorMessage(401, "Unauthorized", body);

        Assert.StartsWith("The API key was rejected.", friendly);
        Assert.Contains("Invalid API key provided.", friendly);
    }

    [Fact]
    public void FallsBackToRawBodyWhenNotJson()
    {
        var friendly = OpenAiChatClient.BuildFriendlyErrorMessage(500, "Internal Server Error", "<html>gateway timeout</html>");

        Assert.StartsWith("The endpoint is having trouble right now.", friendly);
        Assert.Contains("gateway timeout", friendly);
    }

    [Fact]
    public void FallsBackToPrefixOnlyWhenBodyIsEmpty()
    {
        var friendly = OpenAiChatClient.BuildFriendlyErrorMessage(429, "Too Many Requests", "");

        Assert.Equal("Rate limited — too many requests.", friendly);
    }

    [Fact]
    public void UnknownStatusCodeUsesGenericPrefix()
    {
        var friendly = OpenAiChatClient.BuildFriendlyErrorMessage(418, "I'm a teapot", "");

        Assert.Equal("Request failed (418 I'm a teapot).", friendly);
    }
}
