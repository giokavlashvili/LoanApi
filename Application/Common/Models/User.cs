namespace Application.Common.Models
{
    /// <summary>
    /// A user as the application sees one — the projection <see cref="Interfaces.IUserService"/>
    /// returns, deliberately independent of <c>ApplicationUser</c> so nothing outside
    /// <c>Infrastructure</c> depends on ASP.NET Identity's type.
    /// </summary>
    public class User
    {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? BirthDate { get; set; }
    }
}
