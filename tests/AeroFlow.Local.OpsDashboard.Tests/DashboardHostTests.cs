using System.Net;
using AeroFlow.Local.OpsDashboard.Live;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class DashboardHostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Without_messaging_no_listeners_start()
    {
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), h => h.GetType().Name.StartsWith("TopicListener", StringComparison.Ordinal));
        Assert.NotNull(factory.Services.GetRequiredService<AirportViewStore>());
    }

    [Fact]
    public void Snapshot_hydrator_starts_with_the_dashboard()
    {
        Assert.Contains(factory.Services.GetServices<IHostedService>(), h => h is AeroFlow.Local.OpsDashboard.Snapshot.SnapshotHydrator);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/board")]
    [InlineData("/gates")]
    [InlineData("/belts")]
    [InlineData("/events")]
    [InlineData("/ping")]
    public async Task Pages_render(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Board_is_the_fids_kiosk_without_ops_chrome()
    {
        var html = await factory.CreateClient().GetStringAsync("/board");

        Assert.Contains("DEPARTURES", html, StringComparison.Ordinal);
        Assert.Contains("ARRIVALS", html, StringComparison.Ordinal);
        Assert.Contains("fids-clock", html, StringComparison.Ordinal);
        Assert.Contains("Baggage reclaim", html, StringComparison.Ordinal);
        Assert.DoesNotContain("flight-simulator · /scenarios", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"nav\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_keeps_the_ops_workbench()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Overview", html, StringComparison.Ordinal);
        Assert.Contains("class=\"nav\"", html, StringComparison.Ordinal);
        Assert.Contains("Scenario", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fids-clock", html, StringComparison.Ordinal);
    }
}
