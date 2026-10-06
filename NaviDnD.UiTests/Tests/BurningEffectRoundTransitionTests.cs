using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E-регрессия: вне боя обычное свободное действие (не движение по сетке) само может продвинуть
// раунд — герой тратит "Действие", AI патчит time.totalRounds+1 в том же ответе (см. SendAction
// systemPrompt § ДЕЙСТВИЯ И РАУНДЫ). До фикса MapScreenLoop.RunAsync вызывал Storage.TakeRoundBundle()
// только после движения по сетке (стрелками) — после свободного действия (SendAction) периодические
// эффекты (hero.effects[].onRound) НЕ проверялись вообще, и ничего не напоминало AI применить тик
// «Горящего» эффекта на этом раунде. Теперь TakeRoundBundle() проверяется и после SendAction —
// это должно вызвать ВТОРОЙ вызов "SendAction" (FireRoundTransition использует тот же системный
// промпт) с тиком эффекта.
//
// Fixtures/burning_effect_game.json: герой вне боя, hp 10/10, один активный эффект «Горящий» с
// onRound (без onExpire/expiresAtRound — бессрочный, снимается только вручную). Мок SendAction:
// response.json (1й вызов, действие игрока) тратит "Действие" и сам продвигает totalRounds → 2, БЕЗ
// изменения HP — это доказывает, что AI-мок не обязан сам бить себя огнём, движок должен напомнить
// отдельным вызовом; response2.json (2й вызов, FireRoundTransition) наносит 1 урон от «Горящего».
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

        // Не ждать просто текст "Горящий" — он и так виден в легенде с самого начала (имя активного
        // эффекта героя рисуется постоянно), это условие выполнилось бы мгновенно и ничего не
        // доказало бы. Ждём именно текст ВТОРОГО мок-ответа (response2.json) — он появляется только
        // если Storage.TakeRoundBundle() после SendAction реально сработал и вызвал FireRoundTransition.
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
