using Xunit;

// Some tests in this assembly (e.g. OutputHelperTests) capture a command's stdout by
// swapping the process-global Console.Out. That is only safe when no other test runs
// concurrently — otherwise unrelated console writes (such as FluentAssertions' one-time
// licensing banner) leak into the captured text and corrupt it. Running this small unit
// suite sequentially keeps those captures clean; the cost is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
