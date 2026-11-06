namespace Application.Services
{
    public sealed class SignupValidationException : Exception
    {
        public SignupValidationException(string message)
            : base(message)
        {
        }
    }
}
