using NaviDnD.Clients;

namespace NaviDnD.Tests;

public class CodexCliErrorTests
{
    [Theory]
    [InlineData("Not logged in. Please run codex login")]
    [InlineData("Unexpected status 401 Unauthorized")]
    [InlineData("Your authentication token could not be refreshed because your refresh token was already used")]
    [InlineData("invalid_api_key: Incorrect API key provided")]
    [InlineData("stream disconnected; stderr: authentication required")]
    public void AuthenticationFailureExplainsHowToLogIn(string raw)
    {
        var error = Assert.IsType<AiSetupException>(CodexCliAiProvider.CreateError(raw));
        Assert.Contains("не авторизован", error.Message);
        Assert.Contains("codex login", error.Message);
    }

    [Theory]
    [InlineData("403 Forbidden")]
    [InlineData("model_not_found")]
    [InlineData("You've hit your usage limit")]
    public void AccessFailureIsDifferentFromMissingLogin(string raw)
    {
        var error = Assert.IsType<AiSetupException>(CodexCliAiProvider.CreateError(raw));
        Assert.Contains("нет доступа", error.Message);
        Assert.DoesNotContain("не авторизован", error.Message);
    }

    [Theory]
    [InlineData("503 Service unavailable")]
    [InlineData("Connection timed out")]
    public void OtherFailuresKeepTheirActualReason(string raw)
    {
        var error = CodexCliAiProvider.CreateError(raw);
        Assert.IsNotType<AiSetupException>(error);
        Assert.Equal(raw, error.Message);
    }
}
