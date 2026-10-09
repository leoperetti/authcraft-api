namespace Application.Interfaces
{
    public interface IRoleService
    {
        Task<IList<string>> GetRolesAsync(long userId);
        Task<IList<string>> GetAllRolesAsync();
        Task AssignRoleAsync(long userId, string roleName);
    }
}

