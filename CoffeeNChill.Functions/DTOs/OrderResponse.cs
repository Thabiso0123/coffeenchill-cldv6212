using CoffeeNChill.Functions.Models;
using System.Text.Json;

namespace CoffeeNChill.Functions.DTOs
{
    public class OrderResponse
    {
        public string OrderId { get; set; } = string.Empty;

        public string OrderDate { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public List<string> SelectedItemSKUs { get; set; } = new List<string>();

        public double TotalPrice { get; set; }

        public DateTime OrderTimestamp { get; set; }

        public string Status { get; set; } = string.Empty;

        public List<OrderStatusChange> StatusHistory { get; set; } = new List<OrderStatusChange>();

        public DateTime ReceivedAt { get; set; }

        public DateTime LastUpdated { get; set; }

        public static OrderResponse FromEntity(OrderEntity entity)
        {
            List<OrderStatusChange> history;

            try
            {
                history = JsonSerializer.Deserialize<List<OrderStatusChange>>(entity.StatusHistory)
                    ?? new List<OrderStatusChange>();
            }
            catch (JsonException)
            {
                history = new List<OrderStatusChange>();
            }

            return new OrderResponse
            {
                OrderId = entity.RowKey,
                OrderDate = entity.PartitionKey,
                CustomerName = entity.CustomerName,
                SelectedItemSKUs = entity.SelectedItemSKUs
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                TotalPrice = entity.TotalPrice,
                OrderTimestamp = entity.OrderTimestamp,
                Status = entity.Status,
                StatusHistory = history,
                ReceivedAt = entity.ReceivedAt,
                LastUpdated = entity.LastUpdated
            };
        }
    }
}
