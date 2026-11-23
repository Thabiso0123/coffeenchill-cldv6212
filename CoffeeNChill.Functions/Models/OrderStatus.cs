namespace CoffeeNChill.Functions.Models
{
    public static class OrderStatus
    {
        public const string Received = "Received";

        public const string Preparing = "Preparing";

        public const string Ready = "Ready";

        public const string Collected = "Collected";

        public static readonly IReadOnlyList<string> Lifecycle =
            new[] { Received, Preparing, Ready, Collected };

        public static bool TryNormalise(string? value, out string status)
        {
            status = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();

            string? match = Lifecycle.FirstOrDefault(s =>
                string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return false;
            }

            status = match;
            return true;
        }

        public static int IndexOf(string status)
        {
            for (int i = 0; i < Lifecycle.Count; i++)
            {
                if (string.Equals(Lifecycle[i], status, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public static string? Next(string status)
        {
            int index = IndexOf(status);

            if (index < 0 || index >= Lifecycle.Count - 1)
            {
                return null;
            }

            return Lifecycle[index + 1];
        }
    }
}
