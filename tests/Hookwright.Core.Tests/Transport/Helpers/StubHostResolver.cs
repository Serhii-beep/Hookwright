using System.Net;

using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport.Helpers;

internal sealed class StubHostResolver : IHostResolver
{
    private readonly Queue<IPAddress[]> _answers;
    private IPAddress[] _last;

    public StubHostResolver(params IPAddress[][] answers)
    {
        if (answers.Length == 0)
        {
            throw new ArgumentException("At least one answer is required.", nameof(answers));
        }

        _answers = new Queue<IPAddress[]>(answers);
        _last = answers[^1];
    }

    public int Lookups { get; private set; }

    public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Lookups++;

        if (_answers.TryDequeue(out IPAddress[]? answer))
        {
            _last = answer;
        }

        return ValueTask.FromResult<IReadOnlyList<IPAddress>>(_last);
    }
}