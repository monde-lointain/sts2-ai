using System.Text.Json;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class JsonLineLoggerTests
{
    [Fact]
    public void Emits_single_line_with_ts_and_event_fields()
    {
        var clock = new LogicalClock();
        var sw = new StringWriter();
        var logger = new JsonLineLogger(clock, sw);
        logger.Log("hello", new Dictionary<string, object?> { ["foo"] = 42 });

        string output = sw.ToString();
        // One line + trailing newline.
        Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using JsonDocument doc = JsonDocument.Parse(output.TrimEnd());
        Assert.Equal(0, doc.RootElement.GetProperty("ts").GetInt64());
        Assert.Equal("hello", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("foo").GetInt32());
    }

    [Fact]
    public void Ts_is_logical_tick_from_clock()
    {
        var clock = new LogicalClock();
        var sw = new StringWriter();
        var logger = new JsonLineLogger(clock, sw);
        clock.Tick(5);
        logger.Log("e1", new Dictionary<string, object?>());
        clock.Tick(3);
        logger.Log("e2", new Dictionary<string, object?>());

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using JsonDocument d1 = JsonDocument.Parse(lines[0]);
        using JsonDocument d2 = JsonDocument.Parse(lines[1]);
        Assert.Equal(5L, d1.RootElement.GetProperty("ts").GetInt64());
        Assert.Equal(8L, d2.RootElement.GetProperty("ts").GetInt64());
    }

    [Fact]
    public void Reserved_keys_in_payload_are_ignored()
    {
        var clock = new LogicalClock(initialTicks: 7L);
        var sw = new StringWriter();
        var logger = new JsonLineLogger(clock, sw);
        logger.Log(
            "evt",
            new Dictionary<string, object?>
            {
                ["ts"] = 99999, // should be IGNORED — envelope wins
                ["event"] = "spoofed", // should be IGNORED
                ["real"] = "value",
            }
        );

        using JsonDocument doc = JsonDocument.Parse(sw.ToString().TrimEnd());
        Assert.Equal(7L, doc.RootElement.GetProperty("ts").GetInt64());
        Assert.Equal("evt", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("value", doc.RootElement.GetProperty("real").GetString());
    }

    [Fact]
    public void Each_log_call_produces_one_line()
    {
        var clock = new LogicalClock();
        var sw = new StringWriter();
        var logger = new JsonLineLogger(clock, sw);
        for (int i = 0; i < 5; i++)
        {
            logger.Log($"e{i}", new Dictionary<string, object?> { ["i"] = i });
        }

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        foreach (string line in lines)
        {
            using JsonDocument _ = JsonDocument.Parse(line); // valid JSON
        }
    }

    [Fact]
    public void Empty_event_type_throws()
    {
        var clock = new LogicalClock();
        var logger = new JsonLineLogger(clock, new StringWriter());
        Assert.Throws<ArgumentException>(() => logger.Log("", new Dictionary<string, object?>()));
    }

    [Fact]
    public void Null_payload_throws()
    {
        var clock = new LogicalClock();
        var logger = new JsonLineLogger(clock, new StringWriter());
        Assert.Throws<ArgumentNullException>(() => logger.Log("e", null!));
    }

    [Fact]
    public void Defaults_writer_to_stderr_when_none_provided()
    {
        // Smoke: the no-writer ctor returns without throwing.
        var logger = new JsonLineLogger(new LogicalClock());
        Assert.NotNull(logger);
    }

    [Fact]
    public void Concurrent_writes_do_not_interleave()
    {
        var clock = new LogicalClock();
        var sw = new StringWriter();
        var logger = new JsonLineLogger(clock, sw);

        Parallel.For(
            0,
            32,
            i =>
            {
                logger.Log($"e", new Dictionary<string, object?> { ["i"] = i });
            }
        );

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(32, lines.Length);
        foreach (string line in lines)
        {
            // Each line is valid JSON — no torn writes.
            using JsonDocument _ = JsonDocument.Parse(line);
        }
    }
}
