using System.ComponentModel.DataAnnotations;

namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "PendingOperations" section of appsettings.json.
    /// </summary>
    public class PendingOperationOptions
    {
        public const string SectionName = "PendingOperations";

        /// <summary>
        /// How long a parked operation stays confirmable, unless its
        /// <c>[ConfirmableOperation]</c> overrides it.
        /// <para>
        /// Keep it short enough that parked rows drain across a normal deploy. A payload that
        /// outlives its command's shape is the failure the versioned discriminator exists to
        /// catch, and a long lifetime means it has more to catch.
        /// </para>
        /// </summary>
        public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// Unresolved operations one user may hold at once. Without it a caller can park rows
        /// indefinitely, since nothing obliges them to ever confirm one.
        /// </summary>
        [Range(1, 1000)]
        public int MaxPendingPerUser { get; set; } = 20;
    }
}
