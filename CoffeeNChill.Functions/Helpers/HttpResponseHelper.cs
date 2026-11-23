using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace CoffeeNChill.Functions.Helpers
{
    public static class HttpResponseHelper
    {
        private static readonly JsonSerializerOptions SerializerOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public static async Task<HttpResponseData> JsonAsync(
            HttpRequestData req,
            HttpStatusCode statusCode,
            object body,
            IDictionary<string, string>? headers = null)
        {
            HttpResponseData response = req.CreateResponse(statusCode);

            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    response.Headers.Add(header.Key, header.Value);
                }
            }

            string json = JsonSerializer.Serialize(body, body.GetType(), SerializerOptions);

            await response.WriteStringAsync(json);

            response.StatusCode = statusCode;

            return response;
        }

        public static Task<HttpResponseData> ErrorAsync(
            HttpRequestData req,
            HttpStatusCode statusCode,
            string error,
            IEnumerable<string>? details = null)
        {
            return JsonAsync(req, statusCode, new
            {
                error,
                details = details?.ToList() ?? new List<string>()
            });
        }
    }
}
