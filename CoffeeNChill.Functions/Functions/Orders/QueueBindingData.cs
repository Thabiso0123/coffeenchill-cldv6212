using Microsoft.Azure.Functions.Worker;

namespace CoffeeNChill.Functions.Functions.Orders
{
    internal static class QueueBindingData
    {
        public static string GetString(FunctionContext context, string key)
        {
            if (context.BindingContext.BindingData.TryGetValue(key, out object? value) && value != null)
            {
                return value.ToString()?.Trim('"') ?? string.Empty;
            }

            return string.Empty;
        }

        public static long GetLong(FunctionContext context, string key)
        {
            return long.TryParse(GetString(context, key), out long result) ? result : 0;
        }
    }
}
