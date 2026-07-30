namespace Domain.Enums
{
    public enum RefreshTokenStatus
    {
        /// <summary>
        /// Zero is load bearing: the filtered unique index in the entity configuration is written
        /// against <c>[Status] = 0</c>, so renumbering this member silently disables it.
        /// </summary>
        Active = 0,

        /// <summary>Spent by a successful rotation. Presenting it again is treated as a breach.</summary>
        Consumed,

        Revoked
    }
}
