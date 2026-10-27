using Xunit;

// One PostgreSQL container per fixture; fixtures run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
