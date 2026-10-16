using NUnit.Framework;

// Fixtures run side by side, as the suite did under xUnit (one collection per class); tests within a fixture stay sequential.
[assembly: Parallelizable(ParallelScope.Fixtures)]
