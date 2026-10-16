using Application.Interfaces;
using brevo_csharp.Api;
using brevo_csharp.Model;
using Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Application.Services
{
    public class BrevoEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        public BrevoEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void SendInvitation(string toEmail, string subject, string body)
        {
            // 1. Configura la API Key (obtenida de tu panel de Brevo)
            var apiKey = _configuration["Brevo:ApiKey"];
            if (brevo_csharp.Client.Configuration.Default.ApiKey.ContainsKey("api-key"))
            {
                brevo_csharp.Client.Configuration.Default.ApiKey["api-key"] = apiKey;
            }
            else
            {
                brevo_csharp.Client.Configuration.Default.ApiKey.Add("api-key", apiKey);
            }

            // 2. Crea la instancia de la API
            var apiInstance = new TransactionalEmailsApi();

            // 3. Configura el remitente y destinatario
            var fromEmail = _configuration["Brevo:FromEmail"];
            var sender = new SendSmtpEmailSender("Leo Peretti", fromEmail);
            var to = new List<SendSmtpEmailTo>
            {
                new SendSmtpEmailTo(toEmail, "Destinatario")
            };

            // 4. Crea el objeto del email
            var sendSmtpEmail = new SendSmtpEmail(
                sender: sender,
                to: to,
                subject: subject,
                htmlContent: body
            );

            try
            {
                // 5. Envía el correo
                var result = apiInstance.SendTransacEmail(sendSmtpEmail);
                Console.WriteLine("Correo enviado. Message ID: " + result.MessageId);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error al enviar: " + e.Message);
            }
        }
    }
}
