using System.Globalization;
using AeroFlow.Local.FlightEvents;

namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>Display status on the FIDS glass. A later change can add Final Call without touching the event contract.</summary>
public enum FidsStatusKind
{
    Scheduled,
    Boarding,
    Departed,
    Landed,
    Delayed,
    Cancelled,
}

/// <summary>One row on the departures or arrivals board.</summary>
public sealed record FidsFlightRow(
    string FlightId,
    string FlightNumber,
    string Place,
    DateTimeOffset ScheduledTime,
    string Slot,
    string StatusLabel,
    FidsStatusKind Status);

public enum FidsReclaimKind
{
    Waiting,
    FirstBag,
    LastBag,
    Closed,
}

/// <summary>One flight on the baggage reclaim strip under the boards.</summary>
public sealed record FidsReclaimRow(
    string FlightId,
    string FlightNumber,
    string Belt,
    string StageLabel,
    FidsReclaimKind Stage,
    string? Times);

/// <summary>
/// Pure mapping from <see cref="AirportView"/> onto FIDS rows. No I/O, no clock: the board components
/// format times. City names, estimated times and derived Final Call are deliberately left to a follow-up.
/// </summary>
public static class FidsBoardMapper
{
    public const string DefaultHomeAirport = "LHR";
    public const int DefaultBoardRows = 12;
    public const int DefaultReclaimRows = 4;

    public static IReadOnlyList<FidsFlightRow> Departures(
        AirportView view, string homeAirport = DefaultHomeAirport, int maxRows = DefaultBoardRows) =>
        Rows(view, homeAirport, departures: true, maxRows);

    public static IReadOnlyList<FidsFlightRow> Arrivals(
        AirportView view, string homeAirport = DefaultHomeAirport, int maxRows = DefaultBoardRows) =>
        Rows(view, homeAirport, departures: false, maxRows);

    public static IReadOnlyList<FidsReclaimRow> Reclaims(
        AirportView view, int maxRows = DefaultReclaimRows, TimeZoneInfo? zone = null) =>
        view.Belts
            .SelectMany(belt => belt)
            .Select(reclaim => ToReclaim(reclaim, zone))
            .Take(maxRows)
            .ToList();

    public static string StatusLabel(FlightRow flight) => flight.Status switch
    {
        FlightEventType.Delayed => flight.DelayMinutes > 0 ? $"DELAYED +{flight.DelayMinutes}" : "DELAYED",
        FlightEventType.Scheduled when flight.DelayMinutes > 0 => $"SCHEDULED +{flight.DelayMinutes}",
        _ => flight.Status.ToString().ToUpperInvariant(),
    };

    public static FidsStatusKind StatusKind(FlightEventType status) => status switch
    {
        FlightEventType.Boarding => FidsStatusKind.Boarding,
        FlightEventType.Departed => FidsStatusKind.Departed,
        FlightEventType.Landed => FidsStatusKind.Landed,
        FlightEventType.Delayed => FidsStatusKind.Delayed,
        FlightEventType.Cancelled => FidsStatusKind.Cancelled,
        _ => FidsStatusKind.Scheduled,
    };

    public static string ReclaimLabel(ReclaimStage stage) => stage switch
    {
        ReclaimStage.OnBelt => "FIRST BAG",
        ReclaimStage.LastBag => "LAST BAG",
        ReclaimStage.Complete => "CLOSED",
        _ => "WAITING",
    };

    public static FidsReclaimKind ReclaimKind(ReclaimStage stage) => stage switch
    {
        ReclaimStage.OnBelt => FidsReclaimKind.FirstBag,
        ReclaimStage.LastBag => FidsReclaimKind.LastBag,
        ReclaimStage.Complete => FidsReclaimKind.Closed,
        _ => FidsReclaimKind.Waiting,
    };

    public static string? ReclaimTimes(BeltFlight reclaim, TimeZoneInfo? zone = null)
    {
        var parts = new List<string>(2);
        if (reclaim.FirstBagAt is { } first)
        {
            parts.Add($"FIRST {Clock(first, zone)}");
        }

        if (reclaim.LastBagAt is { } last)
        {
            parts.Add($"LAST {Clock(last, zone)}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string Clock(DateTimeOffset time, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTime(time, zone ?? TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string PadSlot(string? value) =>
        value is null or "" ? "TBA" :
        value.All(char.IsAsciiDigit) ? value.PadLeft(2, '0') :
        value;

    private static IReadOnlyList<FidsFlightRow> Rows(AirportView view, string homeAirport, bool departures, int maxRows) =>
        view.Flights.Values
            .Where(f => string.Equals(departures ? f.Origin : f.Destination, homeAirport, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.ScheduledDeparture)
            .Take(maxRows)
            .Select(f => new FidsFlightRow(
                f.FlightId,
                f.FlightNumber,
                departures ? f.Destination : f.Origin,
                f.ScheduledDeparture,
                PadSlot(departures ? f.Gate : f.Belt),
                StatusLabel(f),
                StatusKind(f.Status)))
            .ToList();

    private static FidsReclaimRow ToReclaim(BeltFlight reclaim, TimeZoneInfo? zone) =>
        new(
            reclaim.FlightId,
            reclaim.FlightNumber,
            reclaim.Belt,
            ReclaimLabel(reclaim.Stage),
            ReclaimKind(reclaim.Stage),
            ReclaimTimes(reclaim, zone));
}

/// <summary>Formats the kiosk clock in 24-hour local time, English month names.</summary>
public static class FidsClockFormat
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    public static (string Time, string Date) Format(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return (
            local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            local.ToString("ddd dd MMM yyyy", English).ToUpperInvariant());
    }
}
