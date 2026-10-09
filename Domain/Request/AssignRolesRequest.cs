namespace Domain.Request
{
    public sealed class AssignRolesRequest
    {
        public List<string> Roles { get; init; } = new();
    }
}
