
using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoffeeNChill.Functions.Models
{
    public class MenuItem : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;

        public string RowKey { get; set; } = string.Empty;

        //Menu Item Name
        public string Name { get; set; } = string.Empty;

        //Description of menu item
        public string Description { get; set; } = string.Empty;

        //Selling Price
        public double Price { get; set; }

        //Indicate whether the item is available
        public bool IsAvailable { get; set; }

        //Automatically maintained by Azure Table Storage
        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}
