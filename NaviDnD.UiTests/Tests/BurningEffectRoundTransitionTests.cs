using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E- regression: outside of combat, a normal free action (not moving on the grid) can advance the round
// itself - the hero spends an "Action", AI patches time.totalRounds+1 in the same answer (see SendAction
// systemPrompt § ACTIONS AND ROUNDS). Before the fix, MapScreenLoop.RunAsync called Storage.TakeRoundBundle()
// only after moving on the grid (with arrows) - after a free action (SendAction) periodic effects
// (hero.effects[].onRound) were NOT checked at all, and nothing reminded AI to apply the "Burning" effect tick
// this round. Now TakeRoundBundle() is checked after SendAction - this should cause a SECOND call "SendAction"
// (FireRoundTransition uses the same system prompt) with an effect tick Fixtures/burning_effect_game.json: hero
// out of battle, hp 10/10, one active effect "Burning" with onRound (without onExpire/expiresAtRound -
// indefinite, removed only manually). (1st call, player action) spends “Action” and advances totalRounds → 2,
// WITHOUT changing HP - this proves that AI- does not have to hit itself with fire, the engine should remind
// response2.json with a separate call (2nd call, FireRoundTransition) deals 1 damage from “Burning”.
public class BurningEffectRoundTransitionTests
{
    private readonly ITestOutputHelper _output;

    public BurningEffectRoundTransitionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void FreeTextAction_ThatAdvancesRound_TriggersOnRoundEffectTick()
    {
        using var game = GameSession.Launch(
            fixtureFileName: "burning_effect_game.json", mockedActions: ["SendAction"], mockResponsesFolder: "BurningEffect");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован. Отправляю свободное действие игрока (1й вызов мока — тратит Действие и сам продвигает раунд, без урона)...");
        TestPacing.Step();

        GameConsole.SendText(game.Pid, "жду и осматриваюсь");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Ожидание", 3000),
            "Не дождались спиннера ожидания для действия игрока.");
        _output.WriteLine("Действие обрабатывается. После него движок должен сам обнаружить смену раунда и напомнить об эффекте (2й вызов мока)...");
        TestPacing.Step();

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "Раунд 2", 20000),
            "Раунд не продвинулся до 2 — 1й мок-ответ (действие игрока) не применился как ожидалось.");
        _output.WriteLine("Раунд продвинулся до 2.");

        // Don’t just wait for the text “Burning” - it is already visible in the legend from the very beginning
        // (the name of the hero’s active effect is drawn constantly), this condition would be fulfilled
        // instantly and would not prove anything. We are waiting for the text of the SECOND mock response
        // (response2.json) - it appears only if Storage.TakeRoundBundle() after SendAction actually worked and
        // called FireRoundTransition.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "снова прихватывает кожу", 20000),
            "Не дождались текста 2го мок-ответа (тик эффекта «Горящий») — Storage.TakeRoundBundle() не сработал после свободного действия.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        _output.WriteLine("Проверяю, что урон от эффекта реально применился к HP героя...");
        if (!rows.Any(r => r.Contains("❤ 9/10")))
            failures.Add("После тика «Горящего» HP героя не обновилось до «❤ 9/10» — round-transition патч не применился.");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }
}
