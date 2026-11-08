using Xunit;

// GameConsole.WithGameConsole does FreeConsole()/AttachConsole() at the PROCESS (not thread) level - if xUnit
// runs two tests that control the game in parallel in one test host process, they will tear each other's
// console. Therefore, the entire assembly is strictly sequential, regardless of how many independent
// GameSession- cases appear.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
