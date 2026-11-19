using System.Threading.Channels;

namespace OscCommittee.Web.Services.Grading;

/// <summary>One queued grade. <c>Force</c> skips the identical-bundle reuse path (board retry).</summary>
public sealed record GradingWorkItem(Guid SubmissionId, bool Force);

public interface IGradingQueue
{
    ChannelReader<GradingWorkItem> Reader { get; }

    bool Enqueue(Guid submissionId, bool force = false);
}

/// <summary>
/// In-process queue from PLAN §12.2. Single-server assumption; the worker requeues rows left
/// <c>Uploaded</c> or <c>Grading</c> after a restart.
/// </summary>
public sealed class GradingQueue : IGradingQueue
{
    private readonly Channel<GradingWorkItem> _channel =
        Channel.CreateUnbounded<GradingWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ChannelReader<GradingWorkItem> Reader => _channel.Reader;

    public bool Enqueue(Guid submissionId, bool force = false) =>
        _channel.Writer.TryWrite(new GradingWorkItem(submissionId, force));
}