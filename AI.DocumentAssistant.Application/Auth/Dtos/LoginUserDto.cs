namespace AI.DocumentAssistant.Application.Auth.Dtos
{
    public sealed class LoginUserDto
    {
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
