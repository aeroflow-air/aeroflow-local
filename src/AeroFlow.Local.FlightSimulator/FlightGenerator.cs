using AeroFlow.Local.FlightEvents;

namespace AeroFlow.Local.FlightSimulator;

/// <summary>
/// Pure, seeded state machine: Scheduled → (Delayed →) Boarding → Departed → Landed,
/// with Cancelled as a terminal branch from Scheduled/Delayed. No I/O; easy to test.
/// </summary>
public sealed class FlightGenerator(int seed, TimeProvider clock)
{
    // Two-character IATA designators: flight status and gate allocation accept "XX" + 1-4 digits only.
    private static readonly string[] Airlines = ["AF", "BA", "U2", "FR", "KL", "LH", "VS", "BY"];
    private static readonly string[] Airports = ["LHR", "LGW", "MAN", "EDI", "AMS", "CDG", "FRA", "DUB", "JFK", "MAD", "BCN", "FCO"];
    private const string Home = "LHR";

    private readonly Random _random = new(seed);
    private readonly List<ActiveFlight> _active = [];
    private readonly List<FlightEvent> _injected = [];
    private readonly object _lock = new();
    private int _sequence;

    public int ActiveCount { get { lock (_lock) { return _active.Count; } } }

    /// <summary>Flights a scenario can still act on (not yet boarding), for the dashboard's picker.</summary>
    public IReadOnlyList<DelayableFlight> Delayable()
    {
        lock (_lock)
        {
            return _active
                .Where(f => f.State is FlightEventType.Scheduled or FlightEventType.Delayed)
                .Select(f => new DelayableFlight(f.FlightId, f.FlightNumber, f.Origin, f.Destination, f.ScheduledDeparture, f.DelayMinutes ?? 0))
                .ToList();
        }
    }

    /// <summary>
    /// Demo scenario: delay an active flight by <paramref name="minutes"/> on top of any existing delay. Emits a normal
    /// <c>Delayed</c> lifecycle event, published with the next tick, so every consumer reacts as it would to a simulated delay.
    /// </summary>
    public ScenarioResult Delay(string flightNumber, int minutes, string? reason)
    {
        if (minutes is < 1 or > 600)
        {
            return new ScenarioResult.Invalid("minutes must be between 1 and 600");
        }

        lock (_lock)
        {
            var flight = _active.FirstOrDefault(f => string.Equals(f.FlightNumber, flightNumber, StringComparison.OrdinalIgnoreCase));
            if (flight is null)
            {
                return new ScenarioResult.NotFound($"{flightNumber} is not an active flight");
            }

            if (flight.State is not (FlightEventType.Scheduled or FlightEventType.Delayed))
            {
                return new ScenarioResult.Invalid($"{flight.FlightNumber} is {flight.State} and can no longer be delayed");
            }

            flight.State = FlightEventType.Delayed;
            flight.DelayMinutes = (flight.DelayMinutes ?? 0) + minutes;
            flight.Reason = string.IsNullOrWhiteSpace(reason) ? "Demo scenario" : reason.Trim();
            var evt = ToEvent(flight, clock.GetUtcNow());
            _injected.Add(evt);
            return new ScenarioResult.Accepted(evt);
        }
    }

    public IReadOnlyList<FlightEvent> Tick(SimulatorOptions options)
    {
        lock (_lock)
        {
            return TickLocked(options);
        }
    }

    private IReadOnlyList<FlightEvent> TickLocked(SimulatorOptions options)
    {
        var now = clock.GetUtcNow();
        // Scenario events go out first; a flight delayed by a scenario this tick doesn't also advance this tick.
        var events = new List<FlightEvent>(_injected);
        var justDelayed = _injected.Select(e => e.FlightId).ToHashSet();
        _injected.Clear();

        // Advance existing flights first so a new flight's first event is Scheduled.
        foreach (var flight in _active.ToList())
        {
            if (justDelayed.Contains(flight.FlightId))
            {
                continue;
            }

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

public sealed record DelayableFlight(string FlightId, string FlightNumber, string Origin, string Destination, DateTimeOffset ScheduledDeparture, int DelayMinutes);

public abstract record ScenarioResult
{
    public sealed record Accepted(FlightEvent Event) : ScenarioResult;
    public sealed record NotFound(string Error) : ScenarioResult;
    public sealed record Invalid(string Error) : ScenarioResult;
}
