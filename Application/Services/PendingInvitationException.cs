namespace Application.Services
{
    [Serializable]
    public sealed class PendingInvitationException : InvalidOperationException
    {
        public PendingInvitationException()
            : base("Ya hay una invitación pendiente para este email.")
        {
        }
    }
}
