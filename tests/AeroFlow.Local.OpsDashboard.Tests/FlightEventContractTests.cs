using System.Text.Json;
using AeroFlow.Local.FlightEvents;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

/// <summary>
/// Producer-side contract tests for flight-events (ADR-0019, platform-handbook#111): the canonical
/// examples in contracts/flight-events must round-trip through the producer's own FlightEvent
/// shape with no structural drift — same property names, same types, same nullability, same enums.
/// </summary>
public sealed class FlightEventContractTests
{
    public static TheoryData<string, FlightEventType> Examples => new()
    {
        { "scheduled", FlightEventType.Scheduled },
        { "boarding", FlightEventType.Boarding },
        { "departed", FlightEventType.Departed },
        { "landed", FlightEventType.Landed },
        { "delayed", FlightEventType.Delayed },
        { "cancelled", FlightEventType.Cancelled },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Canonical_example_round_trips_with_the_same_structure(string name, FlightEventType type)
    {
        var json = File.ReadAllText(CanonicalPath(name));
        var evt = JsonSerializer.Deserialize<FlightEvent>(json, FlightEventJson.Options);

        Assert.NotNull(evt);
        Assert.Equal(type, evt!.EventType);
        Assert.False(string.IsNullOrWhiteSpace(evt.FlightId));
        Assert.False(string.IsNullOrWhiteSpace(evt.FlightNumber));

        // Structural match: re-serialising must give the same property set with the same JSON kinds.
        using var canonical = JsonDocument.Parse(json);
        using var roundTripped = JsonDocument.Parse(JsonSerializer.Serialize(evt, FlightEventJson.Options));
        AssertSameStructure(canonical.RootElement, roundTripped.RootElement);
    }

    [Fact]
    public void Every_flight_event_type_has_a_canonical_example()
    {
        var covered = Examples.Select(row => (FlightEventType)row[1]!).ToHashSet();
        Assert.Equal(Enum.GetValues<FlightEventType>().ToHashSet(), covered);
    }

    [Fact]
    public void Delayed_carries_delay_and_reason_and_scheduled_carries_neither()
    {
        var delayed = Read("delayed");
        Assert.Equal((35, "Weather"), (delayed.DelayMinutes, delayed.Reason));
        var scheduled = Read("scheduled");
        Assert.Equal(((int?)null, (string?)null), (scheduled.DelayMinutes, scheduled.Reason));
    }

    private static FlightEvent Read(string name) =>
        JsonSerializer.Deserialize<FlightEvent>(File.ReadAllText(CanonicalPath(name)), FlightEventJson.Options)!;

    private static string CanonicalPath(string name) =>
        Path.Combine(RepoRoot(), "contracts", "flight-events", $"{name}.json");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AeroFlow.Local.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static void AssertSameStructure(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var expectedProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind);
        var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind);
        Assert.Equal(expectedProps, actualProps);
    }
}
