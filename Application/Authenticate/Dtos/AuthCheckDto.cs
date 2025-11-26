namespace Application.Authenticate.Dtos
{
    public class AuthCheckDto
    {
        public bool IsAuthenticated { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
    }
}




