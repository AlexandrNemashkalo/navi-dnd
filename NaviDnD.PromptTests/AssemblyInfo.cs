using Xunit;

// Storage.ValidateKeys кеширует известные поля в статическом (на весь процесс, не per-instance)
// Dictionary<Type,HashSet<string>> — параллельный запуск тестов разных классов из этого проекта
// (разные Storage-инстансы, но один и тот же статический кеш) корректно ловится .NET как
// "Operations that change non-concurrent collections must have exclusive access" и валит тесты,
// которые даже не успевают дойти до настоящего вызова ИИ. Плюс каждый тест пишет в один и тот же
// файл (AppConfig.ProjectRoot резолвится в NaviDnD.PromptTests/Storage/ для всего проекта, см.
// PromptScenario) — параллельные Storage.Save() тоже гонялись бы за одним файлом. Строго
// последовательно, как и NaviDnD.UiTests (см. её AssemblyInfo.cs).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
