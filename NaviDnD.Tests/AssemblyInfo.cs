// Interface and world language (L), saves and folder Storage are common to the process: tests that change them
// should not run in parallel with the others. The typing is fast - sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
