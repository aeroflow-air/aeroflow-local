# Event contract examples

Canonical example messages per event type, owned by the producer of each
topic (ADR-0019). These JSON files are the contract in testable form: the
producer's tests prove its serializer still produces this shape, and every
consumer pins a copy of the examples in its own test suite and proves its
local edge copy can read them. A producer change that renames, removes,
retypes or un-nullables a field therefore fails a build somewhere instead
of dead-lettering at runtime.

## flight-events/ (producer: flight simulator in this repo)

One file per `FlightEventType`: `scheduled`, `boarding`, `departed`,
`landed`, `delayed`, `cancelled`. Field set and nullability follow
`AeroFlow.Local.FlightEvents.FlightEvent` as published with
`FlightEventJson.Options` (web casing, nulls written).

## Updating a contract deliberately

Evolution rules are ADR-0019's: additive optional fields are safe; a new
event type is breaking for consumers (unknown enum values fail
deserialisation on purpose) and is consumer-first.

1. Change the producer's event type and these examples in the producer PR.
   For a new event type, add a new example file.
2. In each consumer repo, copy the changed examples over the pinned ones
   under its contract-test folder, update its local copy if the shape
   changed, and let its contract tests go green. For a new event type,
   consumers must at least tolerate/ignore it **before** the producer
   starts emitting it.
3. Never edit a consumer's pinned copy without the matching producer
   change — the pinned copy is evidence of what the consumer was last
   checked against, not a place to fix the contract.

Consumers of `flight-events` today: svc-gate-allocation, svc-flight-status,
svc-baggage-reclaim (local `FlightEventMessage` copies) and the ops
dashboard in this repo (uses the producer project directly).
