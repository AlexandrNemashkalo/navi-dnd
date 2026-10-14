namespace NaviDnD.UiTests;

// Pause between script steps - so that you can visually follow the automatic run with your eyes. Off by default
// (tests run at full speed); enabled by the environment variable NAVIDND_TEST_STEP_DELAY_MS (ms between steps,
// for example "1500").
internal static class TestPacing
{
    private static readonly int StepDelayMs =
       //5000;
        int.TryParse(Environment.GetEnvironmentVariable("NAVIDND_TEST_STEP_DELAY_MS"), out int v) ? Math.Max(0, v) : 0;

    public static void Step()
    {
        if (StepDelayMs > 0) Thread.Sleep(StepDelayMs);
    }
}
