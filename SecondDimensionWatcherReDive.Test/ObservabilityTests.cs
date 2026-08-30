using System.Diagnostics;
using System.Diagnostics.Metrics;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Observability;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ObservabilityTests
{
    [TestMethod]
    public void SensitiveTagProcessor_RemovesPathsQueriesAndStatements()
    {
        using var activity = new Activity("request").Start();
        activity.SetTag("url.path", "/anime/private-title");
        activity.SetTag("url.query", "token=secret");
        activity.SetTag("db.statement", "SELECT * FROM users");
        activity.SetTag("tool.arguments", "{ secret: true }");
        activity.SetTag("http.route", "/anime/{id}");

        new SensitiveTagRedactionProcessor().OnEnd(activity);

        Assert.IsNull(activity.GetTagItem("url.path"));
        Assert.IsNull(activity.GetTagItem("url.query"));
        Assert.IsNull(activity.GetTagItem("db.statement"));
        Assert.IsNull(activity.GetTagItem("tool.arguments"));
        Assert.AreEqual("/anime/{id}", activity.GetTagItem("http.route"));
    }

    [TestMethod]
    public void DurableJobMetrics_UseOnlyFixedLowCardinalityTags()
    {
        var observedKeys = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, candidate) =>
        {
            if (instrument.Meter.Name == RuntimeTelemetry.MeterName)
                candidate.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                observedKeys.Add(tag.Key);
        });
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                observedKeys.Add(tag.Key);
        });
        listener.Start();
        using var telemetry = new RuntimeTelemetry();

        telemetry.RecordJobAttempt(
            DurableJobType.DownloadCompletion,
            DurableJobStage.MapFiles,
            "retry",
            TimeSpan.FromMilliseconds(20));

        CollectionAssert.AreEquivalent(
            new[] { "job.type", "job.stage", "outcome" },
            observedKeys.ToArray());
        Assert.IsFalse(observedKeys.Any(key =>
            key.Contains("path", StringComparison.OrdinalIgnoreCase)
            || key.Contains("title", StringComparison.OrdinalIgnoreCase)
            || key.Contains("argument", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ScheduledTaskMetrics_NormalizeUnknownTaskIds()
    {
        var observed = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, candidate) =>
        {
            if (instrument.Meter.Name == RuntimeTelemetry.MeterName
                && instrument.Name == "sdw.scheduled_task.runs")
                candidate.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                observed[tag.Key] = tag.Value?.ToString();
        });
        listener.Start();
        using var telemetry = new RuntimeTelemetry();

        telemetry.RecordScheduledTask(
            "private/title/or/tool-arguments",
            "failed",
            TimeSpan.FromMilliseconds(1));

        Assert.AreEqual("other", observed["task.id"]);
        Assert.AreEqual("failed", observed["outcome"]);
        Assert.IsFalse(observed.Values.Contains("private/title/or/tool-arguments"));
    }
}
