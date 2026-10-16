namespace Application.Interfaces
{
    public interface IEmailService
    {
        void SendInvitation(string toEmail, string subject, string body);
    }
}
