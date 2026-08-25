namespace Hookwright.Store.Conformance;

public interface IStoreHarness : IAsyncDisposable
{
    Task InitialiseAsync();

    IStoreSession OpenSession();
}