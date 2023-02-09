using Domain.Common;

namespace Domain.Events
{
    public class UserCreatedEvent : BaseEvent
    {
        public string UserName { get; set; }
        public string? UserMail { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public UserCreatedEvent(string userName, string? userMail = null, string? firstName = null, string? lastName = null)
        {
            this.UserName = userName;
            this.UserMail = userMail;
            this.FirstName = firstName;
            this.LastName = lastName;
        }
    }
}
