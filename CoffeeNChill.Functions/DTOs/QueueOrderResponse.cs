using CoffeeNChill.Functions.Models;

namespace CoffeeNChill.Functions.DTOs
{
    public class QueueOrderResponse
    {
        public string Message { get; set; } = string.Empty;

        public string QueueName { get; set; } = string.Empty;

        public string QueueMessageId { get; set; } = string.Empty;

        public string StatusUrl { get; set; } = string.Empty;

        public OrderMessage Order { get; set; } = new OrderMessage();
    }
}
