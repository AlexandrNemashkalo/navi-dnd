using Xunit;

// GameConsole.WithGameConsole делает FreeConsole()/AttachConsole() на уровне ПРОЦЕССА
// (не потока) — если xUnit запустит два теста, управляющих игрой, параллельно в одном
// test host процессе, они будут рвать друг у друга консоль. Поэтому весь assembly — строго
// последовательно, независимо от того, сколько независимых GameSession-кейсов появится.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
