namespace CoffeeNChill.Functions.DTOs
{
    public class PlaceOrderRequest
    {
        public string? OrderId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public List<string>? SelectedItemSKUs { get; set; }

        public double? TotalPrice { get; set; }
    }
}
