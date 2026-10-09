namespace Application.Interfaces
{
    public interface IUserService
    {
        Task AssignRolesAsync(long userId, IEnumerable<string> roles, CancellationToken ct = default);
    }
}
