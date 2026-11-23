using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using CoffeeNChill.Functions.Constants;
using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CoffeeNChill.Functions.Services
{
    public class QueueStorageService : IQueueStorageService
    {
        private readonly QueueClient _queueClient;
        private readonly QueueClient _poisonQueueClient;
        private readonly ILogger<QueueStorageService> _logger;
        private readonly SemaphoreSlim _initialiseLock = new SemaphoreSlim(1, 1);
        private bool _queueReady;

        public QueueStorageService(
            IConfiguration configuration,
            ILogger<QueueStorageService> logger)
        {
            string connectionString =
                configuration[StorageConstants.ConnectionSettingName]
                ?? throw new InvalidOperationException(
                    "AzureWebJobsStorage connection string is missing.");

            QueueClientOptions options = new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.Base64
            };

            _queueClient = new QueueClient(
                connectionString,
                StorageConstants.OrderQueueName,
                options);

            _poisonQueueClient = new QueueClient(
                connectionString,
                StorageConstants.OrderPoisonQueueName,
                options);

            _logger = logger;
        }

        public async Task<string> SendOrderMessageAsync(OrderMessage message)
        {
            await EnsureQueueExistsAsync();

            string payload = JsonSerializer.Serialize(message);

            Response<SendReceipt> receipt =
                await _queueClient.SendMessageAsync(payload);

            _logger.LogInformation(
                "Order {OrderId} placed on {QueueName} with message id {MessageId}.",
                message.OrderId,
                StorageConstants.OrderQueueName,
                receipt.Value.MessageId);

            return receipt.Value.MessageId;
        }

        public async Task<QueueStatusResponse> GetQueueStatusAsync(int maxMessages)
        {
            int peekCount = Math.Clamp(maxMessages, 1, 32);

            QueueStatusResponse status = new QueueStatusResponse
            {
                QueueName = StorageConstants.OrderQueueName,
                PoisonQueueName = StorageConstants.OrderPoisonQueueName
            };

            status.QueueExists = await _queueClient.ExistsAsync();

            if (status.QueueExists)
            {
                QueueProperties properties =
                    await _queueClient.GetPropertiesAsync();

                status.ApproximateMessageCount = properties.ApproximateMessagesCount;

                PeekedMessage[] peeked =
                    await _queueClient.PeekMessagesAsync(peekCount);

                status.PeekedMessages = peeked
                    .Select(m => new PeekedQueueMessage
                    {
                        MessageId = m.MessageId,
                        InsertedOn = m.InsertedOn,
                        DequeueCount = m.DequeueCount,
                        Body = m.Body.ToString()
                    })
                    .ToList();
            }

            if (await _poisonQueueClient.ExistsAsync())
            {
                QueueProperties poisonProperties =
                    await _poisonQueueClient.GetPropertiesAsync();

                status.PoisonApproximateMessageCount = poisonProperties.ApproximateMessagesCount;
            }

            return status;
        }

        private async Task EnsureQueueExistsAsync()
        {
            if (_queueReady)
            {
                return;
            }

            await _initialiseLock.WaitAsync();

            try
            {
                if (!_queueReady)
                {
                    await _queueClient.CreateIfNotExistsAsync();
                    _queueReady = true;
                }
            }
            finally
            {
                _initialiseLock.Release();
            }
        }
    }
}
