using Application.Common.Interfaces;

namespace Infrastructure.Services
{
    /// <summary>
    /// The one place in the solution permitted to read the system clock.
    /// </summary>
    public sealed class DateTimeService : IDateTime
    {
        // The single sanctioned use of the system clock; see IDateTime for why everything
        // else has to go through this property.
#pragma warning disable RS0030 // Do not use banned APIs
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime LocalNow => DateTime.Now;
#pragma warning restore RS0030
    }
}
