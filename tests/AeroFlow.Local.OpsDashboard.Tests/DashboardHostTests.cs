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
}
