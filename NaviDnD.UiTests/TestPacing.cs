namespace NaviDnD.UiTests;

// Пауза между шагами сценария — чтобы можно было визуально проследить автоматический
// прогон глазами. Выключена по умолчанию (тесты идут на полной скорости); включается
// переменной окружения NAVIDND_TEST_STEP_DELAY_MS (мс между шагами, например "1500").
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
