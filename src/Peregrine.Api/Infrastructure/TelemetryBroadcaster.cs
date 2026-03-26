using Peregrine.Api.Domain;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Peregrine.Api.Infrastructure;

/// <summary>
/// Fanout broadcaster for drone status updates over SSE.
/// Clients subscribe and receive a stream of DroneStatus snapshots.
/// </summary>
public sealed class TelemetryBroadcaster
{
    private readonly List<Channel<DroneStatus>> _subscribers = [];
    private readonly Lock _lock = new();

    public void Publish(DroneStatus status)
    {
        List<Channel<DroneStatus>> dead = [];

        lock (_lock)
        {
            foreach (var channel in _subscribers)
            {
                if (!channel.Writer.TryWrite(status))
                    dead.Add(channel);
            }

            foreach (var channel in dead)
            {
                _subscribers.Remove(channel);
                channel.Writer.TryComplete();
            }
        }
    }

    public async IAsyncEnumerable<DroneStatus> Subscribe(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<DroneStatus>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var status in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return status;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }
            channel.Writer.TryComplete();
        }
    }
}
