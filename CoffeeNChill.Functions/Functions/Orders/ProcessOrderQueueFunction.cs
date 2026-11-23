using CoffeeNChill.Functions.Constants;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class ProcessOrderQueueFunction
    {
        private const int DefaultStageDelaySeconds = 5;
        private const int MaxStageDelaySeconds = 60;

        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<ProcessOrderQueueFunction> _logger;
        private readonly TimeSpan _stageDelay;

        public ProcessOrderQueueFunction(
            IOrderStorageService orderStorageService,
            IConfiguration configuration,
            ILogger<ProcessOrderQueueFunction> logger)
        {
            _orderStorageService = orderStorageService;
            _logger = logger;

            int seconds = int.TryParse(configuration["OrderProcessing:StageDelaySeconds"], out int configured)
                ? Math.Clamp(configured, 0, MaxStageDelaySeconds)
                : DefaultStageDelaySeconds;

            _stageDelay = TimeSpan.FromSeconds(seconds);
        }

        [Function("ProcessOrderQueue")]
        public async Task Run(
            [QueueTrigger(
                StorageConstants.OrderQueueName,
                Connection = StorageConstants.ConnectionSettingName)]
            string queueMessage,
            FunctionContext context)
        {
            string messageId = QueueBindingData.GetString(context, "Id");
            long dequeueCount = QueueBindingData.GetLong(context, "DequeueCount");

            _logger.LogInformation(
                "ProcessOrderQueue triggered for message {MessageId} (attempt {DequeueCount}).",
                messageId,
                dequeueCount);

            OrderMessage order = ParseOrder(queueMessage, messageId);

            OrderEntity entity = await _orderStorageService.CreateReceivedOrderAsync(
                order,
                messageId,
                dequeueCount);

            _logger.LogInformation(
                "Order {OrderId} for {CustomerName} logged in Orders table (PartitionKey {OrderDate}) with status {Status}.",
                entity.RowKey,
                entity.CustomerName,
                entity.PartitionKey,
                entity.Status);

            string? nextStatus = OrderStatus.Next(entity.Status);

            while (nextStatus != null)
            {
                await Task.Delay(_stageDelay, context.CancellationToken);

                OrderStatusUpdateResult result =
                    await _orderStorageService.AdvanceStatusAsync(order.OrderId, nextStatus);

                switch (result.Outcome)
                {
                    case OrderStatusUpdateOutcome.Updated:
                        _logger.LogInformation(
                            "Order {OrderId} moved to {Status}.",
                            order.OrderId,
                            nextStatus);
                        break;

                    case OrderStatusUpdateOutcome.InvalidTransition:
                        _logger.LogInformation(
                            "Order {OrderId} is already at {CurrentStatus}; skipping {Status}.",
                            order.OrderId,
                            result.Order?.Status,
                            nextStatus);
                        break;

                    case OrderStatusUpdateOutcome.NotFound:
                        throw new InvalidOperationException(
                            $"Order {order.OrderId} disappeared from the Orders table during processing.");
                }

                nextStatus = OrderStatus.Next(result.Order?.Status ?? nextStatus);
            }

            _logger.LogInformation(
                "Order {OrderId} completed its lifecycle and is {Status}.",
                order.OrderId,
                OrderStatus.Collected);
        }

        private OrderMessage ParseOrder(string queueMessage, string messageId)
        {
            OrderMessage? order;

            try
            {
                order = JsonSerializer.Deserialize<OrderMessage>(
                    queueMessage,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Queue message {MessageId} is not valid JSON and will be retried before moving to {PoisonQueue}.",
                    messageId,
                    StorageConstants.OrderPoisonQueueName);

                throw new InvalidOperationException(
                    $"Queue message {messageId} could not be parsed as an order.",
                    ex);
            }

            List<string> problems = new List<string>();

            if (order == null)
            {
                problems.Add("message body is empty");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(order.OrderId))
                {
                    problems.Add("OrderId is missing");
                }

                if (string.IsNullOrWhiteSpace(order.CustomerName))
                {
                    problems.Add("CustomerName is missing");
                }

                if (order.SelectedItemSKUs == null || order.SelectedItemSKUs.Count == 0)
                {
                    problems.Add("SelectedItemSKUs is empty");
                }

                if (order.TotalPrice <= 0)
                {
                    problems.Add("TotalPrice must be greater than zero");
                }

                if (order.OrderTimestamp == default)
                {
                    problems.Add("OrderTimestamp is missing");
                }
            }

            if (problems.Count > 0)
            {
                string reason = string.Join("; ", problems);

                _logger.LogError(
                    "Queue message {MessageId} failed validation: {Reason}.",
                    messageId,
                    reason);

                throw new InvalidOperationException(
                    $"Queue message {messageId} is not a valid order: {reason}.");
            }

            OrderMessage validOrder = order!;

            validOrder.OrderTimestamp = DateTime.SpecifyKind(
                validOrder.OrderTimestamp.ToUniversalTime(),
                DateTimeKind.Utc);

            return validOrder;
        }
    }
}
