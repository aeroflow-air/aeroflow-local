namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>
/// Singleton holder of the live <see cref="AirportView"/>: compare-and-swap updates from the listeners, a snapshot for
/// components, and a <see cref="Changed"/> event that components use to re-render.
/// </summary>
public sealed class AirportViewStore
{
    private AirportView _view = AirportView.Empty;

    public event Action? Changed;

    public AirportView Current => Volatile.Read(ref _view);

    public void Update(Func<AirportView, AirportView> change)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _view);
            var next = change(snapshot);
            if (ReferenceEquals(next, snapshot))
            {
                return;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _view, next, snapshot), snapshot))
            {
                Changed?.Invoke();
                return;
            }
        }
    }
}
