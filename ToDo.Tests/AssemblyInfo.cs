using Xunit;

// Loc and SettingsService are process-wide singletons; run tests single-threaded
// so they can't race on each other's static state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
