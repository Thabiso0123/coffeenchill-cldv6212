using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Functions.Models
{
    public class OrderEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;

        public string RowKey { get; set; } = string.Empty;

        public string OrderId { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string SelectedItemSKUs { get; set; } = string.Empty;

        public int ItemCount { get; set; }

        public double TotalPrice { get; set; }

        public DateTime OrderTimestamp { get; set; }

        public string Status { get; set; } = OrderStatus.Received;

        public string StatusHistory { get; set; } = "[]";

        public DateTime ReceivedAt { get; set; }

        public DateTime LastUpdated { get; set; }

        public string QueueMessageId { get; set; } = string.Empty;

        public long DequeueCount { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}
