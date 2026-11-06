namespace Application.Services
{
    public sealed class InvalidStudentInvitationProfileException : Exception
    {
        public InvalidStudentInvitationProfileException()
            : base("El perfil de invitación no es válido para este tenant.")
        {
        }
    }
}
