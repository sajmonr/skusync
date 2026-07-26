using RabbitMQ.Client;

namespace Application.Messaging;

/// <summary>
/// Owns a single, lazily-opened RabbitMQ connection for the out-of-band consumers that need one
/// but that SlimMessageBus doesn't expose its own connection to: the readiness health check and the
/// startup connectivity preflight. The connection is opened once on first use (asynchronously — no
/// blocking) and reused for the process's lifetime; automatic recovery keeps it alive across broker
/// blips. Registered as a singleton so the DI container disposes it on shutdown.
/// </summary>
internal sealed class RabbitMqConnectionProvider(string connectionString) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;

    /// <summary>
    /// Returns the shared connection, opening it on first call. Concurrent first callers are
    /// serialized so exactly one connection is created. If opening fails (broker unreachable) the
    /// exception propagates and nothing is cached, so the next call retries — which is what lets a
    /// readiness probe recover once the broker comes back.
    /// </summary>
    public async Task<IConnection> GetConnection(CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return connection;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            return connection ??= await ApplicationEventBus
                .CreateConnectionFactory(connectionString)
                .CreateConnectionAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        gate.Dispose();
    }
}
