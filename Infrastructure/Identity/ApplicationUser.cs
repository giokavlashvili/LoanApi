using Domain.Common;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Identity
{
    public class ApplicationUser : IdentityUser
    {
        private readonly List<BaseEvent> _domainEvents = new();

        [NotMapped]
        public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();

        public void AddDomainEvent(BaseEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalNumber { get; set; }
        public DateTime? BirthDate { get; set; }
    }
}
