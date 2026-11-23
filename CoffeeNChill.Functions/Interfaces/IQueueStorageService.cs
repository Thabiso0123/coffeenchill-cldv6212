using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Models;

namespace CoffeeNChill.Functions.Interfaces
{
    public interface IQueueStorageService
    {
        Task<string> SendOrderMessageAsync(OrderMessage message);

        Task<QueueStatusResponse> GetQueueStatusAsync(int maxMessages);
    }
}
