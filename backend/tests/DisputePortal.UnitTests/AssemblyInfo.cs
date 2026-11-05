// Tests run one at a time. The OpenTelemetry SDK's AddMeter(name) subscribes to EVERY meter with that name in the
// process, whichever IMeterFactory created it, so a metrics test running in parallel leaks its measurements into another
// test's exporter (DisputeMetricsTests saw 7200 s instead of 5400 s in about one run in six). The whole suite takes well
// under a second, so serial execution costs nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
