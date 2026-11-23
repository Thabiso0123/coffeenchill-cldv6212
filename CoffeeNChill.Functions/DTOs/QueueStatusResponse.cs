namespace CoffeeNChill.Functions.DTOs
{
    public class QueueStatusResponse
    {
        public string QueueName { get; set; } = string.Empty;

        public bool QueueExists { get; set; }

        public int ApproximateMessageCount { get; set; }

        public string PoisonQueueName { get; set; } = string.Empty;

        public int PoisonApproximateMessageCount { get; set; }

        public List<PeekedQueueMessage> PeekedMessages { get; set; } = new List<PeekedQueueMessage>();
    }

    public class PeekedQueueMessage
    {
        public string MessageId { get; set; } = string.Empty;

        public DateTimeOffset? InsertedOn { get; set; }

        public long DequeueCount { get; set; }

        public string Body { get; set; } = string.Empty;
    }
}
