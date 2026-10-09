using Application.Interfaces;
using Domain.Dto;
using Domain.Request;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly IUserService _userService;

        public UsersController(IUserRepository users, IUserService userService)
        {
            _users = users;
            _userService = userService;
        }

        [HttpPut("{userId:int}/assign-roles")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AssignRoles(long userId, [FromBody] AssignRolesRequest request, CancellationToken ct)
        {
            try
            {
                await _userService.AssignRolesAsync(userId, request.Roles, ct);
                return NoContent();
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Determine requester roles
            var requesterRoles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == JwtRegisteredClaimNames.Sub /* no-op to keep selection */)
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            bool isSuper = requesterRoles.Contains("superadmin");
            bool isAdmin = requesterRoles.Contains("admin");

            var allUsers = await _users.GetAllWithRolesAsync();
            var result = new List<UserWithRolesDto>();

            foreach (var u in allUsers)
            {
                var roles = u.UserRoles.Select(ur => ur.Role.Name).ToList();

                // Authorization rules
                if (isSuper)
                {
                    // superadmin can see users with roles admin and user
                    if (!roles.Any(r => r == "admin" || r == "user"))
                        continue;
                }
                else if (isAdmin)
                {
                    // admin can see only users with role user
                    if (!roles.Any(r => r == "user"))
                        continue;
                }
                else
                {
                    // non-admins can only see themselves
                    var currentId = GetCurrentUserId();
                    if (currentId == null || currentId != u.Id)
                        continue;
                }

                result.Add(new UserWithRolesDto
                {
                    Id = u.Id.ToString(),
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    Name = u.Name,
                    Roles = roles
                });
            }

            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var u = await _users.GetByIdWithRolesAsync(id);
            if (u == null) return NotFound();

            var roles = u.UserRoles.Select(ur => ur.Role.Name).ToList();

            // Determine requester roles
            var requesterRoles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            bool isSuper = requesterRoles.Contains("superadmin");
            bool isAdmin = requesterRoles.Contains("admin");

            if (isSuper)
            {
                if (!roles.Any(r => r == "admin" || r == "user"))
                    return Forbid();
            }
            else if (isAdmin)
            {
                if (!roles.Any(r => r == "user"))
                    return Forbid();
            }
            else
            {
                var currentId = GetCurrentUserId();
                if (currentId == null || currentId != u.Id)
                    return Forbid();
            }

            var dto = new UserWithRolesDto
            {
                Id = u.Id.ToString(),
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Name = u.Name,
                Roles = roles
            };

            return Ok(dto);
        }

        private long? GetCurrentUserId()
        {
            var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(sub, out var id)) return id;
            return null;
        }
    }
}
