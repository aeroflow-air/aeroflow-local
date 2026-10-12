using AeroFlow.Local.OpsDashboard.Live;
using Microsoft.AspNetCore.Components;

namespace AeroFlow.Local.OpsDashboard.Components.Panels;

/// <summary>Base for panels: re-renders when the live view changes, at most every 250 ms per circuit.</summary>
public abstract class LiveComponent : ComponentBase, IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);
    private long _lastRender;
    private int _pending;

    [Inject] protected AirportViewStore Store { get; set; } = default!;

    protected AirportView View => Store.Current;

    protected override void OnInitialized() => Store.Changed += OnChanged;

    private void OnChanged()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            var wait = MinInterval - TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond - _lastRender);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait);
            }

            Interlocked.Exchange(ref _pending, 0);
            _lastRender = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        Store.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
