namespace Application.Services
{
    [Serializable]
    public sealed class InvalidInvitationEmailException : ArgumentException
    {
        public InvalidInvitationEmailException()
            : base("El email de la invitación no es válido.", "email")
        {
        }
    }
}
