using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class TokenEstimatorTests
{
    [Fact]
    public void EmptyOrNullTextIsZeroTokens()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokens(""));
        Assert.Equal(0, TokenEstimator.EstimateTokens(null));
    }

    [Fact]
    public void NonEmptyTextIsAtLeastOneToken()
    {
        Assert.Equal(1, TokenEstimator.EstimateTokens("hi"));
    }

    [Fact]
    public void LongerTextEstimatesMoreTokens()
    {
        var shortEstimate = TokenEstimator.EstimateTokens(new string('a', 40));
        var longEstimate = TokenEstimator.EstimateTokens(new string('a', 400));

        Assert.True(longEstimate > shortEstimate);
    }
}
