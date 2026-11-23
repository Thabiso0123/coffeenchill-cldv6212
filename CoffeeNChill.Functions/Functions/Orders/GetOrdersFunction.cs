using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Helpers;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class GetOrdersFunction
    {
        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<GetOrdersFunction> _logger;

        public GetOrdersFunction(
            IOrderStorageService orderStorageService,
            ILogger<GetOrdersFunction> logger)
        {
            _orderStorageService = orderStorageService;
            _logger = logger;
        }

        [Function("GetOrders")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "get",
                Route = "orders")]
            HttpRequestData req)
        {
            string? statusFilter = req.Query["status"];
            string? dateFilter = req.Query["date"];

            _logger.LogInformation(
                "Retrieving orders. Status filter: {Status}, Date filter: {Date}",
                statusFilter,
                dateFilter);

            string? status = null;

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (!OrderStatus.TryNormalise(statusFilter, out string normalisedStatus))
                {
                    return await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.BadRequest,
                        $"Status must be one of: {string.Join(", ", OrderStatus.Lifecycle)}.");
                }

                status = normalisedStatus;
            }

            if (!string.IsNullOrWhiteSpace(dateFilter)
                && !DateTime.TryParseExact(
                    dateFilter,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Date must be in the format yyyy-MM-dd.");
            }

            try
            {
                List<OrderEntity> orders = await _orderStorageService.GetOrdersAsync(status, dateFilter);

                return await HttpResponseHelper.JsonAsync(
                    req,
                    HttpStatusCode.OK,
                    orders.Select(OrderResponse.FromEntity).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.InternalServerError,
                    "An unexpected error occurred while retrieving orders.");
            }
        }
    }
}
