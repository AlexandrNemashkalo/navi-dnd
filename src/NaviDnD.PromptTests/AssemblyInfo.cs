using Xunit;

// Storage.ValidateKeys caches known fields statically (for the entire process, not per-instance)
// Dictionary<Type,HashSet<string>> - parallel launch of tests of different classes from this project (different
// Storage- instances, but the same static cache) is correctly caught by .NET like “Operations that change
// non-concurrent collections must have exclusive access” and fails tests that don’t even have time to reach the
// real AI call. Plus, each test writes to the same file (AppConfig.ProjectRoot resolves to
// NaviDnD.PromptTests/Storage/ for the entire project, see PromptScenario) - parallel Storage.Save() would also
// chase one file Strictly sequentially, like NaviDnD.UiTests (see its AssemblyInfo.cs).).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
