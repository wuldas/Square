using Xunit;

// UI components share process-wide style and reconciliation services that run on one UI thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
