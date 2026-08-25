namespace Hookwright.Store.Conformance;

public abstract class StoreConformance : IAsyncLifetime
{
    private readonly IStoreHarness _harness;

    protected StoreConformance(IStoreHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);

        _harness = harness;
    }

    protected IStoreSession OpenSession()
    {
        return _harness.OpenSession();
    }

    public async ValueTask InitializeAsync()
    {
        await _harness.InitialiseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}