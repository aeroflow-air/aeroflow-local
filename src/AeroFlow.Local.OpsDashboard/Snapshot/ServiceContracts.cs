namespace AeroFlow.Local.OpsDashboard.Snapshot;

// Local copies of the read API responses the dashboard loads on startup (platform-handbook#74). Only the fields the
// dashboard uses; extra fields in the JSON are ignored. Keep in step with:
// - svc-flight-status  GET /api/flights/departures/{airport}, GET /api/flights/arrivals/{airport} (#78)
// - svc-gate-allocation GET /api/gates
// - svc-baggage-reclaim GET /belts (minimal slice of #69)

public sealed record DeparturesBoardDto(string Airport, DateTimeOffset GeneratedAt, IReadOnlyList<DepartureDto> Departures);

public sealed record DepartureDto(
    string FlightNumber,
    string Destination,
    DateTimeOffset ScheduledDeparture,
    string? Gate,
    string Status,
    int DelayMinutes);

public sealed record ArrivalsBoardDto(string Airport, DateTimeOffset GeneratedAt, IReadOnlyList<ArrivalDto> Arrivals);

public sealed record ArrivalDto(
    string FlightNumber,
    string Origin,
    DateTimeOffset ScheduledDeparture,
    string Status,
    int DelayMinutes);

public sealed record GateDto(string Id, string Status, IReadOnlyList<AllocationDto> Allocations, string? ClosedReason = null);

public sealed record AllocationDto(string FlightNumber, string Gate, DateTimeOffset OnBlock, DateTimeOffset OffBlock);

public sealed record BeltDto(string Id, IReadOnlyList<BeltReclaimDto> Reclaims);

public sealed record BeltReclaimDto(
    string FlightId,
    string FlightNumber,
    DateTimeOffset LandedAt,
    string Stage,
    int BagCount,
    int BagsCollected,
    int BagsDelayed);
