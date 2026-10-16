var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Event bus — Azure Service Bus emulator (zero Azure cost; ADR-0013)
// ---------------------------------------------------------------------------
var serviceBus = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

// Emulator health checks need at least one entity modelled.
// Duplicate detection on every topic (ADR-0017): publishers use deterministic MessageIds and retry failed sends,
// so a send that reached the bus but reported failure is dropped here. Consumers still de-duplicate by EventId.
static void DetectDuplicates(Aspire.Hosting.Azure.AzureServiceBusTopicResource topic)
{
    topic.RequiresDuplicateDetection = true;
    topic.DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10);
}

var flightEvents = serviceBus.AddServiceBusTopic("flight-events").WithProperties(DetectDuplicates);
flightEvents.AddServiceBusSubscription("local-dev");

// Real services each get their own subscription (svc-gate-allocation#4, svc-flight-status#6).
flightEvents.AddServiceBusSubscription("sub-svc-gate-allocation", subscriptionName: "gate-allocation");
flightEvents.AddServiceBusSubscription("sub-svc-flight-status", subscriptionName: "flight-status");
flightEvents.AddServiceBusSubscription("sub-svc-baggage-reclaim", subscriptionName: "baggage-reclaim");

// Baggage reclaim owns baggage-events (ADR-0013; platform-handbook#68). The ops dashboard (#53) reads its own copy.
var baggageEvents = serviceBus.AddServiceBusTopic("baggage-events").WithProperties(DetectDuplicates);
baggageEvents.AddServiceBusSubscription("sub-baggage-ops-dashboard", subscriptionName: "ops-dashboard");

// Gate allocation owns gate-events (platform-handbook#77). The ops dashboard reads its own copy of every topic (#73).
var gateEvents = serviceBus.AddServiceBusTopic("gate-events").WithProperties(DetectDuplicates);
gateEvents.AddServiceBusSubscription("sub-gate-ops-dashboard", subscriptionName: "ops-dashboard");
flightEvents.AddServiceBusSubscription("sub-flight-ops-dashboard", subscriptionName: "ops-dashboard");

// One subscription per placeholder so each gets its own copy of every flight event.
string[] placeholderNames = ["svc-turnaround", "svc-cleaning", "svc-catering", "svc-pushback", "svc-terminal"];
foreach (var name in placeholderNames)
{
    flightEvents.AddServiceBusSubscription($"sub-{name}", subscriptionName: name);
}

// ---------------------------------------------------------------------------
// Identity stand-in — Keycloak (free OSS container)
// Why Keycloak: Apache-2.0, no cloud bill, Aspire.Hosting.Keycloak integration,
// real OIDC so services can validate tokens the same way they will with Entra
// later (#51). Admin UI on a stable port for cookie/OIDC stability.
// ---------------------------------------------------------------------------
var keycloak = builder.AddKeycloak("identity", port: 8080);

// ---------------------------------------------------------------------------
// Ops dashboard: live airport UI (Blazor Server, ADR-0016; #53)
// ---------------------------------------------------------------------------
var opsDashboard = builder.AddProject<Projects.AeroFlow_Local_OpsDashboard>("ops-dashboard")
    .WithReference(serviceBus)
    .WithReference(keycloak)
    .WaitFor(serviceBus);

// ---------------------------------------------------------------------------
// Real services (sibling repos) + placeholders for services not yet forked
// ---------------------------------------------------------------------------
var gateAllocation = builder.AddProject<Projects.AeroFlow_GateAllocation>("svc-gate-allocation")
    // Sibling launchSettings pin http://localhost:8080 (same as Keycloak). Clear the
    // host port so Aspire assigns a free one; do not edit the sibling repos for local runs.
    .WithEndpoint("http", e => e.Port = null)
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "gate-allocation")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var flightStatus = builder.AddProject<Projects.AeroFlow_FlightStatus>("svc-flight-status")
    // Same as gate-allocation: sibling pins :8080 which collides with identity/Keycloak.
    .WithEndpoint("http", e => e.Port = null)
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "flight-status")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var baggage = builder.AddProject<Projects.AeroFlow_BaggageReclaim>("svc-baggage-reclaim")
    // Real sibling repo now (platform-handbook#70); like the others its launchSettings pin :8080.
    .WithEndpoint("http", e => e.Port = null)
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "baggage-reclaim")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var turnaround = builder.AddProject<Projects.AeroFlow_Turnaround>("svc-turnaround")
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "svc-turnaround")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var cleaning = builder.AddProject<Projects.AeroFlow_Cleaning>("svc-cleaning")
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "svc-cleaning")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var catering = builder.AddProject<Projects.AeroFlow_Catering>("svc-catering")
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "svc-catering")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var pushback = builder.AddProject<Projects.AeroFlow_Pushback>("svc-pushback")
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "svc-pushback")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

var terminal = builder.AddProject<Projects.AeroFlow_Terminal>("svc-terminal")
    .WithReference(serviceBus)
    .WithEnvironment("FlightEvents__Subscription", "svc-terminal")
    .WithReference(keycloak)
    .WaitFor(serviceBus);

// Ops dashboard (#72, ADR-0016) reads each real service through service discovery: the health strip and
// the startup snapshot of flights, gates and belts (#74).
opsDashboard
    .WithReference(flightStatus)
    .WithReference(gateAllocation)
    .WithReference(baggage);

// ---------------------------------------------------------------------------
// Live flight simulator (#2): publishes lifecycle events to flight-events.
// Tune or pause via Simulator__* env vars here or the project's appsettings.json.
// ---------------------------------------------------------------------------
builder.AddProject<Projects.AeroFlow_Local_FlightSimulator>("flight-simulator")
    .WithReference(serviceBus)
    .WaitFor(serviceBus);

builder.Build().Run();
