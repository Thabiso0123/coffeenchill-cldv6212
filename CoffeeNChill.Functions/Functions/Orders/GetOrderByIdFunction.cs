using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Helpers;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class GetOrderByIdFunction
    {
        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<GetOrderByIdFunction> _logger;

        public GetOrderByIdFunction(
            IOrderStorageService orderStorageService,
            ILogger<GetOrderByIdFunction> logger)
        {
            _orderStorageService = orderStorageService;
            _logger = logger;
        }

        [Function("GetOrderById")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "get",
                Route = "orders/{orderId}")]
            HttpRequestData req,
            string orderId)
        {
            _logger.LogInformation("Retrieving order {OrderId}.", orderId);

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "OrderId is required.");
            }

            try
            {
                OrderEntity? order = await _orderStorageService.GetOrderAsync(orderId.Trim().ToUpperInvariant());

                if (order == null)
                {
                    return await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.NotFound,
                        $"Order {orderId} was not found. It may still be waiting in the queue.");
                }

                return await HttpResponseHelper.JsonAsync(
                    req,
                    HttpStatusCode.OK,
                    OrderResponse.FromEntity(order));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order {OrderId}.", orderId);

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.InternalServerError,
                    "An unexpected error occurred while retrieving the order.");
            }
        }
    }
}
