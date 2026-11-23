namespace CoffeeNChill.Functions.Constants
{
    public static class StorageConstants
    {
        public const string ConnectionSettingName = "AzureWebJobsStorage";

        public const string OrderQueueName = "order-processing-queue";

        public const string OrderPoisonQueueName = "order-processing-queue-poison";

        public const string OrdersTableName = "Orders";

        public const string FailedOrdersTableName = "FailedOrders";

        public const string MenuItemsTableName = "MenuItems";
    }
}
