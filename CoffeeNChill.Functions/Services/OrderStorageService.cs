using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Functions.Constants;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace CoffeeNChill.Functions.Services
{
    public class OrderStorageService : IOrderStorageService
    {
        private const int MaxConcurrencyRetries = 3;

        private readonly TableClient _ordersTable;
        private readonly TableClient _failedOrdersTable;
        private readonly ILogger<OrderStorageService> _logger;
        private readonly SemaphoreSlim _initialiseLock = new SemaphoreSlim(1, 1);
        private bool _tablesReady;

        public OrderStorageService(
            IConfiguration configuration,
            ILogger<OrderStorageService> logger)
        {
            string connectionString =
                configuration[StorageConstants.ConnectionSettingName]
                ?? throw new InvalidOperationException(
                    "AzureWebJobsStorage connection string is missing.");

            _ordersTable = new TableClient(
                connectionString,
                StorageConstants.OrdersTableName);

            _failedOrdersTable = new TableClient(
                connectionString,
                StorageConstants.FailedOrdersTableName);

            _logger = logger;
        }

        public async Task<OrderEntity> CreateReceivedOrderAsync(
            OrderMessage message,
            string queueMessageId,
            long dequeueCount)
        {
            await EnsureTablesExistAsync();

            DateTime now = DateTime.UtcNow;

            OrderEntity entity = new OrderEntity
            {
                PartitionKey = message.OrderTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                RowKey = message.OrderId,
                OrderId = message.OrderId,
                CustomerName = message.CustomerName,
                SelectedItemSKUs = string.Join(",", message.SelectedItemSKUs),
                ItemCount = message.SelectedItemSKUs.Count,
                TotalPrice = message.TotalPrice,
                OrderTimestamp = message.OrderTimestamp,
                Status = OrderStatus.Received,
                StatusHistory = SerializeHistory(new List<OrderStatusChange>
                {
                    new OrderStatusChange { Status = OrderStatus.Received, ChangedAt = now }
                }),
                ReceivedAt = now,
                LastUpdated = now,
                QueueMessageId = queueMessageId,
                DequeueCount = dequeueCount
            };

            try
            {
                await _ordersTable.AddEntityAsync(entity);
                return entity;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(
                    "Order {OrderId} already exists in the Orders table. Resuming from its current status.",
                    message.OrderId);

                Response<OrderEntity> existing =
                    await _ordersTable.GetEntityAsync<OrderEntity>(
                        entity.PartitionKey,
                        entity.RowKey);

                return existing.Value;
            }
        }

        public async Task<OrderEntity?> GetOrderAsync(string orderId)
        {
            await EnsureTablesExistAsync();

            string filter = TableClient.CreateQueryFilter($"RowKey eq {orderId}");

            await foreach (OrderEntity order in _ordersTable.QueryAsync<OrderEntity>(filter, maxPerPage: 1))
            {
                return order;
            }

            return null;
        }

        public async Task<List<OrderEntity>> GetOrdersAsync(string? status, string? orderDate)
        {
            await EnsureTablesExistAsync();

            List<string> filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(orderDate))
            {
                filters.Add(TableClient.CreateQueryFilter($"PartitionKey eq {orderDate}"));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                filters.Add(TableClient.CreateQueryFilter($"Status eq {status}"));
            }

            string? filter = filters.Count == 0 ? null : string.Join(" and ", filters);

            List<OrderEntity> orders = new List<OrderEntity>();

            await foreach (OrderEntity order in _ordersTable.QueryAsync<OrderEntity>(filter))
            {
                orders.Add(order);
            }

            return orders
                .OrderByDescending(o => o.OrderTimestamp)
                .ToList();
        }

        public async Task<OrderStatusUpdateResult> AdvanceStatusAsync(string orderId, string newStatus)
        {
            for (int attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                OrderEntity? order = await GetOrderAsync(orderId);

                if (order == null)
                {
                    return OrderStatusUpdateResult.NotFound();
                }

                if (OrderStatus.IndexOf(newStatus) <= OrderStatus.IndexOf(order.Status))
                {
                    return OrderStatusUpdateResult.InvalidTransition(order);
                }

                DateTime now = DateTime.UtcNow;

                List<OrderStatusChange> history = DeserializeHistory(order.StatusHistory);
                history.Add(new OrderStatusChange { Status = newStatus, ChangedAt = now });

                order.Status = newStatus;
                order.StatusHistory = SerializeHistory(history);
                order.LastUpdated = now;

                try
                {
                    await _ordersTable.UpdateEntityAsync(
                        order,
                        order.ETag,
                        TableUpdateMode.Replace);

                    return OrderStatusUpdateResult.Updated(order);
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    _logger.LogWarning(
                        "Concurrency conflict updating order {OrderId} to {Status} on attempt {Attempt}.",
                        orderId,
                        newStatus,
                        attempt);
                }
            }

            throw new InvalidOperationException(
                $"Order {orderId} could not be updated to {newStatus} after {MaxConcurrencyRetries} attempts.");
        }

        public async Task RecordFailedMessageAsync(
            string rawMessage,
            string queueMessageId,
            long dequeueCount,
            string reason)
        {
            await EnsureTablesExistAsync();

            DateTime now = DateTime.UtcNow;

            FailedOrderEntity entity = new FailedOrderEntity
            {
                PartitionKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                RowKey = string.IsNullOrWhiteSpace(queueMessageId)
                    ? Guid.NewGuid().ToString()
                    : queueMessageId,
                QueueMessageId = queueMessageId,
                RawMessage = rawMessage,
                Reason = reason,
                DequeueCount = dequeueCount,
                FailedAt = now
            };

            await _failedOrdersTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        private async Task EnsureTablesExistAsync()
        {
            if (_tablesReady)
            {
                return;
            }

            await _initialiseLock.WaitAsync();

            try
            {
                if (!_tablesReady)
                {
                    await _ordersTable.CreateIfNotExistsAsync();
                    await _failedOrdersTable.CreateIfNotExistsAsync();
                    _tablesReady = true;
                }
            }
            finally
            {
                _initialiseLock.Release();
            }
        }

        private static string SerializeHistory(List<OrderStatusChange> history) =>
            JsonSerializer.Serialize(history);

        private static List<OrderStatusChange> DeserializeHistory(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<OrderStatusChange>>(json)
                    ?? new List<OrderStatusChange>();
            }
            catch (JsonException)
            {
                return new List<OrderStatusChange>();
            }
        }
    }
}
