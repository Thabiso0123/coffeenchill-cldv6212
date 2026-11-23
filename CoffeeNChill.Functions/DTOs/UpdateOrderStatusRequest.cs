namespace CoffeeNChill.Functions.DTOs
{
    public class UpdateOrderStatusRequest
    {
        public string OrderId { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }
}
