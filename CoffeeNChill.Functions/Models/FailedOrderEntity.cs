using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Functions.Models
{
    public class FailedOrderEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;

        public string RowKey { get; set; } = string.Empty;

        public string QueueMessageId { get; set; } = string.Empty;

        public string RawMessage { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public long DequeueCount { get; set; }

        public DateTime FailedAt { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}
