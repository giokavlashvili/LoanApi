namespace Application.Common.Logging
{
    /// <summary>
    /// Marks a property whose value must never reach a log sink. Applied on top of the
    /// name based rules in <see cref="LogRedactor"/>, for secrets that are not named
    /// like one (e.g. an account number or a signed payload).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SensitiveDataAttribute : Attribute
    {
    }
}
