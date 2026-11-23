using Azure;
using CoffeeNChill.Functions.Constants;
using CoffeeNChill.Functions.DTOs;
using CoffeeNChill.Functions.Helpers;
using CoffeeNChill.Functions.Interfaces;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoffeeNChill.Functions.Functions.Orders
{
    public class QueueOrderFunction
    {
        private const int MaxItemsPerOrder = 20;
        private const int MaxCustomerNameLength = 100;
        private const double PriceTolerance = 0.01;

        private static readonly Regex OrderIdPattern =
            new Regex("^ORD-[0-9]{4}-[A-Z0-9]{4,12}$", RegexOptions.Compiled);

        private static readonly Regex SkuPattern =
            new Regex("^[A-Z0-9]{2,10}-[A-Z0-9]{1,10}$", RegexOptions.Compiled);

        private readonly IQueueStorageService _queueStorageService;
        private readonly ITableStorageService _tableStorageService;
        private readonly IOrderStorageService _orderStorageService;
        private readonly ILogger<QueueOrderFunction> _logger;

        public QueueOrderFunction(
            IQueueStorageService queueStorageService,
            ITableStorageService tableStorageService,
            IOrderStorageService orderStorageService,
            ILogger<QueueOrderFunction> logger)
        {
            _queueStorageService = queueStorageService;
            _tableStorageService = tableStorageService;
            _orderStorageService = orderStorageService;
            _logger = logger;
        }

        [Function("QueueOrder")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "post",
                Route = "orders/queue")]
            HttpRequestData req)
        {
            _logger.LogInformation("Received a new order for queueing.");

            PlaceOrderRequest? request;

            try
            {
                request = await JsonSerializer.DeserializeAsync<PlaceOrderRequest>(
                    req.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Order request contained invalid JSON.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "The request body contains invalid JSON.");
            }

            if (request == null)
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Invalid request body.");
            }

            List<string> errors = ValidateRequest(request, out List<string> skus, out string? orderId);

            if (errors.Count > 0)
            {
                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Order validation failed.",
                    errors);
            }

            try
            {
                List<MenuItem> menuItems = await _tableStorageService.GetMenuItemsBySkusAsync(skus);

                Dictionary<string, MenuItem> menuLookup = menuItems
                    .GroupBy(m => m.RowKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                List<string> unknownSkus = skus
                    .Where(s => !menuLookup.ContainsKey(s))
                    .Distinct()
                    .ToList();

                if (unknownSkus.Count > 0)
                {
                    return await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.BadRequest,
                        "One or more selected items do not exist on the menu.",
                        unknownSkus.Select(s => $"Unknown SKU: {s}"));
                }

                List<string> unavailableSkus = skus
                    .Where(s => !menuLookup[s].IsAvailable)
                    .Distinct()
                    .ToList();

                if (unavailableSkus.Count > 0)
                {
                    return await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.BadRequest,
                        "One or more selected items are currently unavailable.",
                        unavailableSkus.Select(s => $"Unavailable SKU: {s}"));
                }

                double calculatedTotal = Math.Round(
                    skus.Sum(s => menuLookup[s].Price),
                    2,
                    MidpointRounding.AwayFromZero);

                if (request.TotalPrice.HasValue
                    && Math.Abs(request.TotalPrice.Value - calculatedTotal) > PriceTolerance)
                {
                    return await HttpResponseHelper.ErrorAsync(
                        req,
                        HttpStatusCode.BadRequest,
                        "TotalPrice does not match the menu prices of the selected items.",
                        new[]
                        {
                            $"Submitted: {request.TotalPrice.Value.ToString("0.00", CultureInfo.InvariantCulture)}",
                            $"Expected: {calculatedTotal.ToString("0.00", CultureInfo.InvariantCulture)}"
                        });
                }

                DateTime now = DateTime.UtcNow;
                DateTime orderTimestamp = new DateTime(
                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

                if (orderId != null)
                {
                    OrderEntity? existing = await _orderStorageService.GetOrderAsync(orderId);

                    if (existing != null)
                    {
                        return await HttpResponseHelper.ErrorAsync(
                            req,
                            HttpStatusCode.Conflict,
                            $"An order with id {orderId} already exists.");
                    }
                }

                OrderMessage message = new OrderMessage
                {
                    OrderId = orderId ?? GenerateOrderId(orderTimestamp),
                    CustomerName = request.CustomerName.Trim(),
                    SelectedItemSKUs = skus,
                    TotalPrice = calculatedTotal,
                    OrderTimestamp = orderTimestamp
                };

                string queueMessageId = await _queueStorageService.SendOrderMessageAsync(message);

                QueueOrderResponse body = new QueueOrderResponse
                {
                    Message = "Order accepted and queued for processing.",
                    QueueName = StorageConstants.OrderQueueName,
                    QueueMessageId = queueMessageId,
                    StatusUrl = $"/api/orders/{message.OrderId}",
                    Order = message
                };

                return await HttpResponseHelper.JsonAsync(
                    req,
                    HttpStatusCode.Accepted,
                    body,
                    new Dictionary<string, string>
                    {
                        ["Location"] = body.StatusUrl
                    });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(
                    ex,
                    "Storage request failed while queueing order. Status: {Status}, Code: {ErrorCode}",
                    ex.Status,
                    ex.ErrorCode);

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.ServiceUnavailable,
                    "The order queue is currently unavailable. Please try again shortly.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AggregateException)
            {
                _logger.LogError(ex, "Could not connect to storage while queueing order.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.ServiceUnavailable,
                    "The order queue could not be reached. Please try again shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while queueing order.");

                return await HttpResponseHelper.ErrorAsync(
                    req,
                    HttpStatusCode.InternalServerError,
                    "An unexpected error occurred while queueing the order.");
            }
        }

        private static List<string> ValidateRequest(
            PlaceOrderRequest request,
            out List<string> skus,
            out string? orderId)
        {
            List<string> errors = new List<string>();
            skus = new List<string>();
            orderId = null;

            if (string.IsNullOrWhiteSpace(request.CustomerName))
            {
                errors.Add("CustomerName is required.");
            }
            else if (request.CustomerName.Trim().Length > MaxCustomerNameLength)
            {
                errors.Add($"CustomerName cannot exceed {MaxCustomerNameLength} characters.");
            }

            if (request.SelectedItemSKUs == null || request.SelectedItemSKUs.Count == 0)
            {
                errors.Add("SelectedItemSKUs must contain at least one item.");
            }
            else if (request.SelectedItemSKUs.Count > MaxItemsPerOrder)
            {
                errors.Add($"An order cannot contain more than {MaxItemsPerOrder} items.");
            }
            else
            {
                foreach (string? sku in request.SelectedItemSKUs)
                {
                    if (string.IsNullOrWhiteSpace(sku))
                    {
                        errors.Add("SelectedItemSKUs cannot contain empty values.");
                        continue;
                    }

                    string normalised = sku.Trim().ToUpperInvariant();

                    if (!SkuPattern.IsMatch(normalised))
                    {
                        errors.Add($"SKU '{sku}' is not in a valid format (e.g. COF-001).");
                        continue;
                    }

                    skus.Add(normalised);
                }
            }

            if (request.TotalPrice.HasValue && request.TotalPrice.Value <= 0)
            {
                errors.Add("TotalPrice must be greater than zero when supplied.");
            }

            if (!string.IsNullOrWhiteSpace(request.OrderId))
            {
                string normalisedId = request.OrderId.Trim().ToUpperInvariant();

                if (!OrderIdPattern.IsMatch(normalisedId))
                {
                    errors.Add("OrderId must follow the format ORD-YYYY-XXXX (e.g. ORD-2026-8801).");
                }
                else
                {
                    orderId = normalisedId;
                }
            }

            return errors;
        }

        private static string GenerateOrderId(DateTime timestamp)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            return $"ORD-{timestamp:yyyy}-{suffix}";
        }
    }
}
