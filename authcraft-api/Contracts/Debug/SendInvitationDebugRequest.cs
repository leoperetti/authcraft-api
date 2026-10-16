namespace authcraft_api.Contracts.Debug
{
    public sealed class SendInvitationDebugRequest
    {
        public string ToEmail { get; set; } = string.Empty;

        public string Subject { get; set; } = "Invitación de prueba";

        public string Body { get; set; } = "<p>Invitación de prueba enviada desde el endpoint temporal de depuración.</p>";
    }
}
