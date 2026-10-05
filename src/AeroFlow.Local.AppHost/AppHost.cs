var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Event bus — Azure Service Bus emulator (zero Azure cost; ADR-0013)
// ---------------------------------------------------------------------------
var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

// Emulator health checks need at least one entity modelled.
var flightEvents = serviceBus.AddServiceBusTopic("flight-events");
flightEvents.AddServiceBusSubscription("local-dev");

// ---------------------------------------------------------------------------
// Identity stand-in — Keycloak (free OSS container)
// Why Keycloak: Apache-2.0, no cloud bill, Aspire.Hosting.Keycloak integration,
// real OIDC so services can validate tokens the same way they will with Entra
// later (#51). Admin UI on a stable port for cookie/OIDC stability.
// ---------------------------------------------------------------------------
var keycloak = builder.AddKeycloak("identity", port: 8080);

// ---------------------------------------------------------------------------
// Ops dashboard API (placeholder)
// ---------------------------------------------------------------------------
var opsDashboard = builder.AddProject<Projects.AeroFlow_Local_OpsDashboard>("ops-dashboard")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

// ---------------------------------------------------------------------------
// Real services (sibling repos) + placeholders for services not yet forked
// ---------------------------------------------------------------------------
var gateAllocation = builder.AddProject<Projects.AeroFlow_GateAllocation>("svc-gate-allocation")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var flightStatus = builder.AddProject<Projects.AeroFlow_FlightStatus>("svc-flight-status")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var baggage = builder.AddProject<Projects.AeroFlow_BaggageReclaim>("svc-baggage-reclaim")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var turnaround = builder.AddProject<Projects.AeroFlow_Turnaround>("svc-turnaround")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var cleaning = builder.AddProject<Projects.AeroFlow_Cleaning>("svc-cleaning")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var catering = builder.AddProject<Projects.AeroFlow_Catering>("svc-catering")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var pushback = builder.AddProject<Projects.AeroFlow_Pushback>("svc-pushback")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var terminal = builder.AddProject<Projects.AeroFlow_Terminal>("svc-terminal")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

builder.Build().Run();
