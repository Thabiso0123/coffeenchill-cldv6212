using CoffeeNChill.Functions.Constants;
using CoffeeNChill.Functions.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class ProcessOrderPoisonQueueFunction
    {
        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<ProcessOrderPoisonQueueFunction> _logger;

        public ProcessOrderPoisonQueueFunction(
            IOrderStorageService orderStorageService,
            ILogger<ProcessOrderPoisonQueueFunction> logger)
        {
            _orderStorageService = orderStorageService;
            _logger = logger;
        }

        [Function("ProcessOrderPoisonQueue")]
        public async Task Run(
            [QueueTrigger(
                StorageConstants.OrderPoisonQueueName,
                Connection = StorageConstants.ConnectionSettingName)]
            string poisonMessage,
            FunctionContext context)
        {
            string messageId = QueueBindingData.GetString(context, "Id");
            long dequeueCount = QueueBindingData.GetLong(context, "DequeueCount");

            _logger.LogCritical(
                "Order message {MessageId} was moved to {PoisonQueue} after exceeding the retry limit. Payload: {Payload}",
                messageId,
                StorageConstants.OrderPoisonQueueName,
                poisonMessage);

            try
            {
                await _orderStorageService.RecordFailedMessageAsync(
                    poisonMessage,
                    messageId,
                    dequeueCount,
                    $"Message exceeded the maximum dequeue count on {StorageConstants.OrderQueueName}.");

                _logger.LogWarning(
                    "Poison message {MessageId} recorded in the {Table} table for staff review.",
                    messageId,
                    StorageConstants.FailedOrdersTableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to record poison message {MessageId} in the {Table} table.",
                    messageId,
                    StorageConstants.FailedOrdersTableName);
            }
        }
    }
}
