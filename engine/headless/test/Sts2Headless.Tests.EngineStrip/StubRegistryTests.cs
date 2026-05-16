// StubRegistry validation per S2 brief:
//   - capture scope records categories correctly
//   - Reset works
//   - concurrent xUnit calls don't lose data

using System.Collections.Concurrent;
using Sts2Headless.EngineStrip;

namespace Sts2Headless.Tests.EngineStrip;

[Collection("StubRegistry")]
public class StubRegistryTests
{
    public StubRegistryTests()
    {
        StubRegistry.Reset();
    }

    [Fact]
    public void Record_OutsideCaptureScope_LandsInGlobalHits()
    {
        StubRegistry.Record(StubCategory.Rendering, "T", "M");
        Assert.Contains(StubRegistry.GlobalHits, h => h.Type == "T" && h.Member == "M");
    }

    [Fact]
    public void Capture_RoutesHits_AndDisposeReverts()
    {
        using (var capture = StubRegistry.Capture())
        {
            StubRegistry.Record(StubCategory.Audio, "A", "B");
            Assert.Single(capture.Hits);
            Assert.Contains(StubCategory.Audio, capture.Categories);
            Assert.DoesNotContain(StubRegistry.GlobalHits, h => h.Type == "A");
        }
        StubRegistry.Record(StubCategory.Audio, "A", "After");
        Assert.Contains(StubRegistry.GlobalHits, h => h.Member == "After");
    }

    [Fact]
    public void Capture_NestedScopes_RestoreParentOnDispose()
    {
        using var outer = StubRegistry.Capture();
        StubRegistry.Record(StubCategory.Rendering, "Outer", "M");

        using (var inner = StubRegistry.Capture())
        {
            StubRegistry.Record(StubCategory.Audio, "Inner", "M");
            Assert.Single(inner.Hits);
            Assert.Contains(StubCategory.Audio, inner.Categories);
            Assert.DoesNotContain(StubCategory.Audio, outer.Categories);
        }

        StubRegistry.Record(StubCategory.Rendering, "Outer", "After");
        Assert.Equal(2, outer.Hits.Count);
        Assert.Contains(outer.Hits, h => h.Member == "After");
    }

    [Fact]
    public void Reset_ClearsGlobalHitsAndCounter()
    {
        StubRegistry.Record(StubCategory.Rendering, "T", "A");
        StubRegistry.Record(StubCategory.Rendering, "T", "B");
        Assert.NotEmpty(StubRegistry.GlobalHits);

        StubRegistry.Reset();

        Assert.Empty(StubRegistry.GlobalHits);
        StubRegistry.Record(StubCategory.Rendering, "T", "C");
        Assert.Equal(1L, StubRegistry.GlobalHits[0].Counter);
    }

    [Fact]
    public void Counter_IsMonotonicAcrossHits()
    {
        StubRegistry.Record(StubCategory.Rendering, "T", "A");
        StubRegistry.Record(StubCategory.Rendering, "T", "B");
        StubRegistry.Record(StubCategory.Rendering, "T", "C");

        var counters = StubRegistry.GlobalHits.Select(h => h.Counter).ToArray();
        Assert.Equal(counters.OrderBy(c => c), counters);
        Assert.Equal(3, counters.Distinct().Count());
    }

    [Fact]
    public void FormatNotStubbed_NamesCategoryTypeAndMember()
    {
        var msg = StubRegistry.FormatNotStubbed(StubCategory.Animation, "Tween", "FooBar");
        Assert.Contains("Sts2Headless.EngineStrip", msg);
        Assert.Contains("Tween.FooBar", msg);
        Assert.Contains("Animation", msg);
        Assert.Contains("was not stubbed", msg);
    }

    [Fact]
    public void ThrowNotStubbed_ThrowsNotImplementedWithFormattedMessage()
    {
        var ex = Assert.Throws<NotImplementedException>(() =>
            StubRegistry.ThrowNotStubbed(StubCategory.Localization, "Tr", "Missing")
        );
        Assert.Contains("Tr.Missing", ex.Message);
        Assert.Contains("Localization", ex.Message);
    }

    [Fact]
    public async Task ConcurrentCaptures_AreIsolated_PerAsyncFlow()
    {
        // 16 concurrent tasks each open their own Capture scope and record N hits in it.
        // Each task asserts its own scope sees only its own hits.
        const int taskCount = 16;
        const int hitsPerTask = 50;
        var errors = new ConcurrentBag<string>();

        await Task.WhenAll(
            Enumerable
                .Range(0, taskCount)
                .Select(taskId =>
                    Task.Run(() =>
                    {
                        using var capture = StubRegistry.Capture();
                        for (int i = 0; i < hitsPerTask; i++)
                        {
                            StubRegistry.Record(StubCategory.Rendering, $"Task{taskId}", $"M{i}");
                        }
                        if (capture.Hits.Count != hitsPerTask)
                        {
                            errors.Add(
                                $"task {taskId} saw {capture.Hits.Count} hits, expected {hitsPerTask}"
                            );
                        }
                        if (capture.Hits.Any(h => h.Type != $"Task{taskId}"))
                        {
                            errors.Add($"task {taskId} saw cross-task hits");
                        }
                    })
                )
        );

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ConcurrentGlobalRecords_AllLand()
    {
        // With no Capture scopes anywhere, fan out concurrent records and ensure the
        // global queue receives all of them — proves the ConcurrentQueue is sound.
        const int taskCount = 16;
        const int hitsPerTask = 100;

        await Task.WhenAll(
            Enumerable
                .Range(0, taskCount)
                .Select(taskId =>
                    Task.Run(() =>
                    {
                        for (int i = 0; i < hitsPerTask; i++)
                        {
                            StubRegistry.Record(StubCategory.Audio, $"Task{taskId}", $"M{i}");
                        }
                    })
                )
        );

        Assert.Equal(taskCount * hitsPerTask, StubRegistry.GlobalHits.Count);
    }
}
