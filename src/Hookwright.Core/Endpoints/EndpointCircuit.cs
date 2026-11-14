using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Endpoints;

/// <summary>
/// One endpoint's circuit breaker and concurrency limit: how many deliveries may be in flight to it
/// at once, and whether any may go at all.
/// </summary>
/// <remarks>
/// <para>
/// Failure cuts the load. The limit starts at the configured concurrency. From the third failure in a
/// row it halves with every further one, to a floor of one. At the tenth the circuit opens, and nothing
/// goes until the cooldown is over, when exactly one probe is admitted. A probe that fails reopens the
/// circuit for another cooldown. A probe that is answered closes it at a limit of one, which doubles
/// with every answer until the configured concurrency is back. Any answer, accepted or refused, clears
/// the failure count: the endpoint is up. Only retryable failures count against it. A 410, or a delivery
/// that could not be sent says nothing about its health.
/// </para>
/// <para>
/// The circuit is per instance, like the concurrency it bounds. The persisted failure count seeds a
/// circuit on an instance that has not seen the endpoint yet, so that a restart does not unleash
/// everything at once on an endpoint that was already failing. Safe to use from every worker at once.
/// </para>
/// </remarks>
public sealed class EndpointCircuit
{
    /// <summary>
    /// Failures in a row from which the limit is halved with each further one.
    /// </summary>
    public const int DegradeAfter = 3;

    /// <summary>
    /// Failures in a row at which the circuit opens.
    /// </summary>
    public const int OpenAfter = 10;

    private readonly Lock _gate = new();
    private readonly int _maxConcurrency;
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _disableAfter;
    private int _failures;
    private DateTimeOffset? _failingSince;
    private int _limit;
    private int _inFlight;
    private bool _open;
    private bool _probing;
    private DateTimeOffset _reopensAt;

    /// <summary>
    /// Creates the circuit for one endpoint.
    /// </summary>
    /// <param name="maxConcurrency">The most deliveries in flight to the endpoint at once while it is healthy.</param>
    /// <param name="cooldown">How long an open circuit stays open before it admits a probe.</param>
    /// <param name="disableAfter">How long the endpoint may fail without an answer before it should be disabled.</param>
    /// <param name="consecutiveFailures">The endpoint's persisted failures in a row.</param>
    /// <param name="failingSince">When the endpoint's current run of failures began, if it is in one.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is not positive, or the failure count is negative.</exception>
    public EndpointCircuit(
          int maxConcurrency,
          TimeSpan cooldown,
          TimeSpan disableAfter,
          int consecutiveFailures,
          DateTimeOffset? failingSince)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cooldown, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(disableAfter, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        _maxConcurrency = maxConcurrency;
        _cooldown = cooldown;
        _disableAfter = disableAfter;
        _failures = consecutiveFailures;
        _failingSince = failingSince;
        _limit = LimitFor(consecutiveFailures, maxConcurrency);
        _open = consecutiveFailures >= OpenAfter;
        _reopensAt = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// The most deliveries that may be in flight at once while the circuit is closed.
    /// </summary>
    public int Limit
    {
        get
        {
            lock (_gate)
            {
                return _limit;
            }
        }
    }

    /// <summary>
    /// Deliveries admitted and not yet recorded or released.
    /// </summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>
    /// Retryable failures since the endpoint last answered, as this instance has seen them.
    /// </summary>
    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate)
            {
                return _failures;
            }
        }
    }

    /// <summary>
    /// When the current run of failures began, or <see langword="null"/> while the endpoint is answering.
    /// </summary>
    public DateTimeOffset? FailingSince
    {
        get
        {
            lock (_gate)
            {
                return _failingSince;
            }
        }
    }

    /// <summary>
    /// When an open circuit next admits a probe, or <see langword="null"/> while it is closed.
    /// </summary>
    public DateTimeOffset? ReopensAt
    {
        get
        {
            lock (_gate)
            {
                return _open ? _reopensAt : null;
            }
        }
    }

    /// <summary>
    /// How the endpoint is doing, as this instance sees it.
    /// </summary>
    public EndpointHealth Health
    {
        get
        {
            lock (_gate)
            {
                return HealthLocked();
            }
        }
    }

    /// <summary>
    /// Asks to send one delivery to the endpoint now.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>
    /// <see cref="Admission.Granted"/>, holding a slot until <see cref="Record"/> or <see cref="Release"/>
    /// gives it back, otherwise <see cref="Admission.Refused"/>, saying when an open circuit reopens.
    /// </returns>
    public Admission TryAdmit(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_open)
            {
                if (_probing || now < _reopensAt)
                {
                    return new Admission.Refused(_probing ? null : _reopensAt);
                }

                _probing = true;
                _inFlight++;

                return new Admission.Granted(isProbe: true);
            }

            if (_inFlight >= _limit)
            {
                return new Admission.Refused(null);
            }

            _inFlight++;

            return new Admission.Granted(isProbe: false);
        }
    }

    /// <summary>
    /// Gives an admitted delivery's slot back and learns from how its attempt went.
    /// </summary>
    /// <param name="admission">The admission the delivery went out under.</param>
    /// <param name="outcome">What the attempt produced.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>What changed.</returns>
    public CircuitChange Record(Admission.Granted admission, AttemptOutcome outcome, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(admission);

        lock (_gate)
        {
            EndpointHealth before = HealthLocked();
            Leave(admission);

            if (_open && !admission.IsProbe)
            {
                return CircuitChange.None;
            }

            switch (AttemptOutcomes.HealthSignalOf(outcome))
            {
                case HealthSignal.Answered:
                    Answered();
                    break;
                case HealthSignal.Failed:
                    if (Failed(now))
                    {
                        return CircuitChange.Exhausted;
                    }

                    break;
                default:
                    return CircuitChange.None;
            }

            return ChangeBetween(before, HealthLocked());
        }
    }

    /// <summary>
    /// Gives an admitted delivery's slot back without learning anything because no attempt was made.
    /// </summary>
    /// <param name="admission">The admission the delivery was given.</param>
    public void Release(Admission.Granted admission)
    {
        ArgumentNullException.ThrowIfNull(admission);

        lock (_gate)
        {
            Leave(admission);
        }
    }

    private static int LimitFor(int failures, int maxConcurrency)
    {
        if (failures < DegradeAfter)
        {
            return maxConcurrency;
        }

        int halvings = Math.Min(failures - DegradeAfter + 1, 30);

        return Math.Max(1, maxConcurrency >> halvings);
    }

    private static CircuitChange ChangeBetween(EndpointHealth before, EndpointHealth after)
    {
        return (before, after) switch
        {
            (EndpointHealth.Healthy, EndpointHealth.Degraded) => CircuitChange.Degraded,
            (not EndpointHealth.Failing, EndpointHealth.Failing) => CircuitChange.Opened,
            (EndpointHealth.Recovering, EndpointHealth.Degraded or EndpointHealth.Healthy) => CircuitChange.Closed,
            (EndpointHealth.Degraded, EndpointHealth.Healthy) => CircuitChange.Recovered,
            _ => CircuitChange.None
        };
    }

    private EndpointHealth HealthLocked()
    {
        if (_open)
        {
            return _probing ? EndpointHealth.Recovering : EndpointHealth.Failing;
        }

        return _limit < _maxConcurrency || _failures >= DegradeAfter
            ? EndpointHealth.Degraded
            : EndpointHealth.Healthy;
    }

    private void Leave(Admission.Granted admission)
    {
        if (_inFlight > 0)
        {
            _inFlight--;
        }

        if (admission.IsProbe)
        {
            _probing = false;
        }
    }

    private void Answered()
    {
        _failures = 0;
        _failingSince = null;

        if (_open)
        {
            _open = false;
            _limit = 1;
        }
        else if (_limit < _maxConcurrency)
        {
            _limit = Math.Min(_maxConcurrency, _limit * 2);
        }
    }

    private bool Failed(DateTimeOffset now)
    {
        _failures++;
        _failingSince ??= now;

        if (_open)
        {
            _reopensAt = now + _cooldown;
        }
        else if (_failures >= OpenAfter)
        {
            _open = true;
            _reopensAt = now + _cooldown;
        }
        else if (_failures >= DegradeAfter)
        {
            _limit = Math.Max(1, _limit / 2);
        }

        return now - _failingSince.Value >= _disableAfter;
    }
}