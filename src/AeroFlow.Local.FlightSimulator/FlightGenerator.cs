using AeroFlow.Local.FlightEvents;

namespace AeroFlow.Local.FlightSimulator;

/// <summary>
/// Pure, seeded state machine: Scheduled → (Delayed →) Boarding → Departed → Landed,
/// with Cancelled as a terminal branch from Scheduled/Delayed. No I/O; easy to test.
/// </summary>
public sealed class FlightGenerator(int seed, TimeProvider clock)
{
    private static readonly string[] Airlines = ["AF", "BA", "EZY", "RYR", "KLM", "DLH", "VIR", "TOM"];
    private static readonly string[] Airports = ["LHR", "LGW", "MAN", "EDI", "AMS", "CDG", "FRA", "DUB", "JFK", "MAD", "BCN", "FCO"];
    private const string Home = "LHR";

    private readonly Random _random = new(seed);
    private readonly List<ActiveFlight> _active = [];
    private int _sequence;

    public int ActiveCount => _active.Count;

    public IReadOnlyList<FlightEvent> Tick(SimulatorOptions options)
    {
        var now = clock.GetUtcNow();
        var events = new List<FlightEvent>();

        // Advance existing flights first so a new flight's first event is Scheduled.
        foreach (var flight in _active.ToList())
        {
            var next = Advance(flight, options);
            if (next is null)
            {
                continue;
            }

            flight.State = next.Value;
            events.Add(ToEvent(flight, now));
            if (next is FlightEventType.Landed or FlightEventType.Cancelled)
            {
                _active.Remove(flight);
            }
        }

        if (_active.Count < options.MaxActiveFlights && _random.NextDouble() < options.NewFlightProbability)
        {
            var flight = NewFlight(now);
            _active.Add(flight);
            events.Add(ToEvent(flight, now));
        }

        return events;
    }

    private FlightEventType? Advance(ActiveFlight flight, SimulatorOptions options)
    {
        switch (flight.State)
        {
            case FlightEventType.Scheduled:
                var roll = _random.NextDouble();
                if (roll < options.CancelProbability)
                {
                    flight.Reason = "Operational";
                    return FlightEventType.Cancelled;
                }

                if (roll < options.CancelProbability + options.DelayProbability)
                {
                    flight.DelayMinutes = _random.Next(10, 121);
                    flight.Reason = Pick(["Weather", "ATC", "Late inbound aircraft", "Crew"]);
                    return FlightEventType.Delayed;
                }

                return FlightEventType.Boarding;
            case FlightEventType.Delayed:
                if (_random.NextDouble() < options.CancelProbability)
                {
                    flight.Reason = "Delay exceeded crew hours";
                    return FlightEventType.Cancelled;
                }

                return FlightEventType.Boarding;
            case FlightEventType.Boarding:
                return FlightEventType.Departed;
            case FlightEventType.Departed:
                return FlightEventType.Landed;
            default:
                return null;
        }
    }

    private ActiveFlight NewFlight(DateTimeOffset now)
    {
        _sequence++;
        var outbound = _random.NextDouble() < 0.5;
        var other = Pick(Airports.Where(a => a != Home).ToArray());
        return new ActiveFlight
        {
            FlightId = $"SIM-{_sequence:D5}",
            FlightNumber = $"{Pick(Airlines)}{_random.Next(100, 9999)}",
            Origin = outbound ? Home : other,
            Destination = outbound ? other : Home,
            ScheduledDeparture = now.AddMinutes(_random.Next(30, 240)),
            State = FlightEventType.Scheduled,
        };
    }

    private string Pick(string[] values) => values[_random.Next(values.Length)];

    private FlightEvent ToEvent(ActiveFlight f, DateTimeOffset now) => new(
        EventId: new Guid(Bytes()),
        FlightId: f.FlightId,
        FlightNumber: f.FlightNumber,
        EventType: f.State,
        Origin: f.Origin,
        Destination: f.Destination,
        ScheduledDeparture: f.ScheduledDeparture,
        OccurredAt: now,
        DelayMinutes: f.DelayMinutes,
        Reason: f.State is FlightEventType.Delayed or FlightEventType.Cancelled ? f.Reason : null);

    // Seeded event ids keep a whole run repeatable for the same seed.
    private byte[] Bytes()
    {
        var b = new byte[16];
        _random.NextBytes(b);
        return b;
    }

    private sealed class ActiveFlight
    {
        public required string FlightId { get; init; }
        public required string FlightNumber { get; init; }
        public required string Origin { get; init; }
        public required string Destination { get; init; }
        public required DateTimeOffset ScheduledDeparture { get; init; }
        public FlightEventType State { get; set; }
        public int? DelayMinutes { get; set; }
        public string? Reason { get; set; }
    }
}
