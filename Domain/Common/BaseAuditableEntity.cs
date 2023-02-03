namespace Domain.Common
{
    public abstract class BaseAuditableEntity : BaseEntity
    {
        public DateTime Created { get; private set; }

        public string? CreatedBy { get; private set; }

        public DateTime? LastModified { get; private set; }

        public string? LastModifiedBy { get; private set; }
    }
}