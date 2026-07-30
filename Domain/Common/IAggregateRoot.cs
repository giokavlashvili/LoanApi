namespace Domain.Common
{
    /// <summary>
    /// Marks an entity as the entry point to an aggregate — the only kind of object a
    /// repository may load or persist. Entities inside an aggregate are reached through their
    /// root, never fetched independently, so the root can enforce the invariants that span them.
    /// </summary>
    public interface IAggregateRoot;
}
