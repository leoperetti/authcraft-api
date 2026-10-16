using Microsoft.AspNetCore.Mvc;
using Application.Interfaces;
using System.Security.Claims;

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
            var tenantClientId = User.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenantClientId))
            {
                return Unauthorized("El token no contiene el claim 'tenant' (client_id)");
            }

            var roles = await _roleService.GetAllRolesAsync(tenantClientId);
            return Ok(roles);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(long userId)
        {
            var roles = await _roleService.GetRolesAsync(userId);
            return Ok(roles);
        }
    }
}
