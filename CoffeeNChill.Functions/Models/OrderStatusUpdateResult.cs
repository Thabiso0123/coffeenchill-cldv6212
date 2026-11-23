namespace CoffeeNChill.Functions.Models
{
    public enum OrderStatusUpdateOutcome
    {
        Updated,
        NotFound,
        InvalidTransition
    }

    public class OrderStatusUpdateResult
    {
        public OrderStatusUpdateOutcome Outcome { get; set; }

        public OrderEntity? Order { get; set; }

        public static OrderStatusUpdateResult Updated(OrderEntity order) =>
            new OrderStatusUpdateResult { Outcome = OrderStatusUpdateOutcome.Updated, Order = order };

        public static OrderStatusUpdateResult NotFound() =>
            new OrderStatusUpdateResult { Outcome = OrderStatusUpdateOutcome.NotFound };

        public static OrderStatusUpdateResult InvalidTransition(OrderEntity order) =>
            new OrderStatusUpdateResult { Outcome = OrderStatusUpdateOutcome.InvalidTransition, Order = order };
    }
}
