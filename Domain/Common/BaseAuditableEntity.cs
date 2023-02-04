namespace Domain.Common
{
    public abstract class BaseAuditableEntity : BaseEntity
    {
        public DateTime Created { get; protected set; }

        public string? CreatedBy { get; protected set; }

        public DateTime? LastModified { get; protected set; }

        public string? LastModifiedBy { get; protected set; }
    }
}