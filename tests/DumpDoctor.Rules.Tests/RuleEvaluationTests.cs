using DumpDoctor.Core;
using DumpDoctor.Rules;

namespace DumpDoctor.Rules.Tests;

public sealed class RuleEvaluationTests
{
    [Fact]
    public void LargeObjectFootprintRule_ProducesFinding_WhenThresholdExceeded()
    {
        var store = new EvidenceStore();
        store.Add(new GcSnapshotEvidence("gc", "GC snapshot", "collector", "artifact", true, true, 4, 8));
        store.Add(new LargeObjectEvidence("loh:1", "Large object", "collector", "artifact", 0x10, "System.Byte[]", 120_000_000, 2));

        var findings = new LargeObjectFootprintRule().Evaluate(store, new RulePolicy());

        var finding = Assert.Single(findings);
        Assert.Equal("MEM-003", finding.RuleId);
        Assert.Equal(Severity.Medium, finding.Severity);
    }

    [Fact]
    public void SyncOverAsyncRule_ProducesFinding_WhenRepeatedWaitsExist()
    {
        var store = new EvidenceStore();
        for (var i = 0; i < 3; i++)
        {
            store.Add(new ThreadSnapshotEvidence(
                $"thread:{i}",
                "Thread snapshot",
                "collector",
                "artifact",
                (uint)i,
                i,
                true,
                true,
                "Running",
                [
                    new StackFrameEvidence(0, 0, "System.Threading.Tasks.Task.Wait()"),
                    new StackFrameEvidence(0, 0, "MyApp.Worker.Execute()")
                ]));
        }

        var findings = new SyncOverAsyncRule().Evaluate(store, new RulePolicy());

        var finding = Assert.Single(findings);
        Assert.Equal("TP-002", finding.RuleId);
        Assert.Equal(Severity.Medium, finding.Severity);
    }
}
