namespace Domain.Entities
{
    /// <summary>
    /// Max lengths of the bounded <see cref="Log"/> columns. The SQL Server sink fails a whole
    /// batch when a value overflows ("String or binary data would be truncated"), reporting
    /// only to SelfLog, so every writer truncates to these before the event is created.
    /// Keep in sync with <c>Infrastructure/Persistence/Configurations/LogConfiguration.cs</c>.
    /// </summary>
    public static class LogColumnLimits
    {
        public const int Level = 16;
        public const int Logger = 255;
        public const int CorrelationId = 64;
        public const int Method = 10;
        public const int Url = 2048;
        public const int UserId = 128;
        public const int UserName = 256;
        public const int ClientIp = 64;
        public const int MachineName = 128;
        public const int Channel = 20;

        public static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value[..maxLength];
        }
    }
}
