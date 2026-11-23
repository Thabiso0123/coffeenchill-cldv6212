using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Helpers;
using CoffeeNChill.Functions.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class GetQueueStatusFunction
    {
        private const int DefaultPeekCount = 10;

        private readonly IQueueStorageService _queueStorageService;
        private readonly ILogger<GetQueueStatusFunction> _logger;

        public GetQueueStatusFunction(
            IQueueStorageService queueStorageService,
            ILogger<GetQueueStatusFunction> logger)
        {
            _queueStorageService = queueStorageService;
            _logger = logger;
        }

        [Function("GetQueueStatus")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "get",
                Route = "orders/queue/status")]
            HttpRequestData req)
        {
            int peek = int.TryParse(req.Query["peek"], out int requested)
                ? requested
                : DefaultPeekCount;

            try
            {
                QueueStatusResponse status = await _queueStorageService.GetQueueStatusAsync(peek);

                return await HttpResponseHelper.JsonAsync(req, HttpStatusCode.OK, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading the order queue status.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.ServiceUnavailable,
                    "The order queue could not be reached.");
            }
        }
    }
}
