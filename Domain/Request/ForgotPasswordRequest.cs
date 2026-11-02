namespace Domain.Request
{
    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = default!;
        public string ClientId { get; set; } = default!;
    }
}
