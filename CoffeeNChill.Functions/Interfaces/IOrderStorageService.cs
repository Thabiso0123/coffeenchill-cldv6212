using CoffeeNChill.Functions.Models;

namespace CoffeeNChill.Functions.Interfaces
{
    public interface IOrderStorageService
    {
        Task<OrderEntity> CreateReceivedOrderAsync(
            OrderMessage message,
            string queueMessageId,
            long dequeueCount);

        Task<OrderEntity?> GetOrderAsync(string orderId);

        Task<List<OrderEntity>> GetOrdersAsync(string? status, string? orderDate);

        Task<OrderStatusUpdateResult> AdvanceStatusAsync(string orderId, string newStatus);

        Task RecordFailedMessageAsync(
            string rawMessage,
            string queueMessageId,
            long dequeueCount,
            string reason);
    }
}
