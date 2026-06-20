// Several tests (e.g. OutputHelperTests and the Leave command tests with --json)
// capture or write to the process-wide Console.Out/Console.Error. Running them in
// parallel races on that shared global state and produces flaky failures, so the
// whole assembly runs sequentially. The suite is small and fast.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
