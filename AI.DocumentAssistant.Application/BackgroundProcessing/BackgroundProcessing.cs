using System.Threading.Channels;
using AI.DocumentAssistant.Application.Abstractions.Documents;

namespace AI.DocumentAssistant.Infrastructure.BackgroundProcessing;

public sealed class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _channel;

    public DocumentProcessingQueue()
    {
        _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(documentId, cancellationToken);
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}