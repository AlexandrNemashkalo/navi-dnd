using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AiCliDiscoveryTests
{
    [Fact]
    public void MissingConfiguredExecutableDoesNotFallBackToAnotherInstallation()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "codex.exe");
        Assert.False(AiCliDiscovery.IsAvailable("codex", path));
        Assert.False(AiCliDiscovery.IsAvailable("claude", path));
    }

    [Fact]
    public void CustomExecutableIsRecognizedWithoutLaunchingIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        try
        {
            File.WriteAllText(path, "test fixture");
            Assert.True(AiCliDiscovery.IsAvailable("codex", path));
        }
        finally { File.Delete(path); }
    }
}
