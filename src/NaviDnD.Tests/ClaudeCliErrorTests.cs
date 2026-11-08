using NaviDnD.Clients;

namespace NaviDnD.Tests;

public class ClaudeCliErrorTests
{
    [Theory]
    [InlineData("Not logged in · Please run /login")]
    [InlineData("API Error: 401 authentication_error")]
    [InlineData("OAuth token has expired. Please login again.")]
    [InlineData("Invalid authentication credentials")]
    public void AuthenticationFailureShowsActionableSetupError(string error)
    {
        var exception = Assert.IsType<AiSetupException>(ClaudeCliAiProvider.CreateError(error));
        Assert.Contains("не авторизован", exception.Message);
        Assert.Contains("claude auth login", exception.Message);
    }

    [Fact]
    public void AccessDeniedDoesNotClaimUserIsLoggedOut()
    {
        var exception = Assert.IsType<AiSetupException>(
            ClaudeCliAiProvider.CreateError("organization does not have access to Claude"));
        Assert.Contains("не имеет доступа", exception.Message);
        Assert.DoesNotContain("не авторизован", exception.Message);
    }

    [Theory]
    [InlineData("API Error: 529 overloaded")]
    [InlineData("API Error: 403 model unavailable")]
    [InlineData("network connection failed")]
    public void OtherFailuresRemainOrdinaryErrors(string error)
    {
        var exception = ClaudeCliAiProvider.CreateError(error);
        Assert.IsNotType<AiSetupException>(exception);
        Assert.Equal(error, exception.Message);
    }
}
