// One shared database and Redis: tests run one at a time and use their own sessions and rows.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
