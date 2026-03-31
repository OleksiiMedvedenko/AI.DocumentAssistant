namespace AI.DocumentAssistant.Application.Auth.Dtos
{
    public sealed class RegisterUserDto
    {
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
