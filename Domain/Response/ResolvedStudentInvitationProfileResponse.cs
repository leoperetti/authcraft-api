namespace Domain.Response
{
    public class ResolvedStudentInvitationProfileResponse
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Dni { get; set; }
    }
}
