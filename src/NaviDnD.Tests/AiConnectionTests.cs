using System.Text;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AiConnectionTests
{
    [Theory]
    [InlineData(0, "", "Logged in using ChatGPT", AiConnectionStatus.Connected)]
    [InlineData(1, "", "Not logged in", AiConnectionStatus.NotAuthorized)]
    [InlineData(0, "Not logged in", "", AiConnectionStatus.NotAuthorized)]
    [InlineData(1, "", "connection timeout", AiConnectionStatus.CheckFailed)]
    public void CodexStatusUsesExitCodeAndBothStreams(int code, string output, string errors, AiConnectionStatus expected) =>
        Assert.Equal(expected, AiConnection.ParseStatus("codex", code, output, errors).Status);

    [Theory]
    [InlineData("{\"loggedIn\":true,\"email\":\"player@example.test\"}", AiConnectionStatus.Connected, "player@example.test")]
    [InlineData("{\"loggedIn\":false}", AiConnectionStatus.NotAuthorized, "")]
    [InlineData("{\"error\":\"bad credentials\"}", AiConnectionStatus.CheckFailed, "")]
    public void ClaudeStatusKeepsAccountSeparateFromConnection(string json, AiConnectionStatus expected, string account)
    {
        var result = AiConnection.ParseStatus("claude", 0, json, "");
        Assert.Equal(expected, result.Status);
        Assert.Equal(account, result.Account);
    }

    [Fact]
    public void AccountMetadataNeverReturnsTheCredential()
    {
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"email":"player@example.test","secret":"private"}"""))
            .TrimEnd('=').Replace('+','-').Replace('/','_');
        Assert.Equal("player@example.test", AiConnection.AccountFromIdToken("header."+payload+".signature"));
        Assert.Equal("", AiConnection.AccountFromIdToken("invalid"));
        Assert.Equal("", AiConnection.AccountFromIdToken("header.e30.signature"));
    }
}
