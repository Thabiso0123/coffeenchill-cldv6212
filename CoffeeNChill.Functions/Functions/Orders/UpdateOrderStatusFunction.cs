using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Helpers;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class UpdateOrderStatusFunction
    {
        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<UpdateOrderStatusFunction> _logger;

        public UpdateOrderStatusFunction(
            IOrderStorageService orderStorageService,
            ILogger<UpdateOrderStatusFunction> logger)
        {
            _orderStorageService = orderStorageService;
            _logger = logger;
        }

        [Function("UpdateOrderStatus")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "put",
                Route = "orders/status")]
            HttpRequestData req)
        {
            UpdateOrderStatusRequest? request;

            try
            {
                request = await JsonSerializer.DeserializeAsync<UpdateOrderStatusRequest>(
                    req.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Order status update contained invalid JSON.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "The request body contains invalid JSON.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.OrderId))
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "OrderId is required.");
            }

            if (!OrderStatus.TryNormalise(request.Status, out string newStatus))
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    $"Status must be one of: {string.Join(", ", OrderStatus.Lifecycle)}.");
            }

            string orderId = request.OrderId.Trim().ToUpperInvariant();

            _logger.LogInformation(
                "Updating order {OrderId} to status {Status}.",
                orderId,
                newStatus);

            try
            {
                OrderStatusUpdateResult result =
                    await _orderStorageService.AdvanceStatusAsync(orderId, newStatus);

                return result.Outcome switch
                {
                    OrderStatusUpdateOutcome.Updated => await HttpResponseHelper.JsonAsync(
                        req,
                        HttpStatusCode.OK,
                        OrderResponse.FromEntity(result.Order!)),

                    OrderStatusUpdateOutcome.NotFound => await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.NotFound,
                        $"Order {orderId} was not found."),

                    _ => await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.Conflict,
                        $"Order {orderId} is already {result.Order?.Status}. Status can only move forward through {string.Join(" -> ", OrderStatus.Lifecycle)}.")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for order {OrderId}.", orderId);

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.InternalServerError,
                    "An unexpected error occurred while updating the order status.");
            }
        }
    }
}
