using CoffeeNChill.Functions.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace CoffeeNChill.Functions.Functions.MenuItemFunction
{
    public class GetMenuItemsByCategoryFunction
    {
        private readonly ITableStorageService _tableStorageService;
        private readonly ILogger<GetMenuItemsByCategoryFunction> _logger;

        public GetMenuItemsByCategoryFunction(
            ITableStorageService tableStorageService,
            ILogger<GetMenuItemsByCategoryFunction> logger)
        {
            _tableStorageService = tableStorageService;
            _logger = logger;
        }

        [Function("GetMenuItemsByCategory")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(
                AuthorizationLevel.Anonymous,
                "get",
                Route = "menu/category/{category}")]
            HttpRequestData req,
            string category)
        {
            _logger.LogInformation(
                "Retrieving menu items for category: {Category}",
                category);

            //_logger.LogInformation(
            //      "***** CATEGORY FUNCTION CALLED ***** Category = {Category}",
            //      category);

            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    var badRequest =
                        req.CreateResponse(HttpStatusCode.BadRequest);

                    await badRequest.WriteStringAsync(
                        "Category is required.");

                    return badRequest;
                }

                var menuItems =
                    await _tableStorageService
                        .GetMenuItemsByCategoryAsync(category);

                var response =
                    req.CreateResponse(HttpStatusCode.OK);

                await response.WriteAsJsonAsync(menuItems);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error retrieving menu items for category: {Category}",
                    category);

                var response =
                    req.CreateResponse(
                        HttpStatusCode.InternalServerError);

                await response.WriteStringAsync(
                    "An error occurred while retrieving menu items by category.");

                return response;
            }
        }
    }
}