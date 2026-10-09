using Microsoft.AspNetCore.Mvc;
using Application.Interfaces;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _roleService;
        public RolesController(IRoleService roleService) => _roleService = roleService;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var roles = await _roleService.GetAllRolesAsync();
            return Ok(roles);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(long userId)
        {
            var roles = await _roleService.GetRolesAsync(userId);
            return Ok(roles);
        }
    }

    public record AssignRoleDto(long UserId, string RoleName);
}
