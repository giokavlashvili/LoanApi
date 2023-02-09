namespace Application.Authenticate.Dtos
{
    public class LoginDto
    {
        public string AccessToken { get; set; }
        public DateTime ValidTo { get; set; }
    }
}
