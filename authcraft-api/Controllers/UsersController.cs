using Application.Audit;
using Application.Interfaces;
using Domain.Audit;
using Domain.Dto;
using Domain.Request;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "superadmin,admin")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuditService _auditService;

        public UsersController(IUserService userService, IAuditService auditService)
        {
            _userService = userService;
            _auditService = auditService;
        }

        [HttpPut("{userId:int}/assign-roles")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AssignRoles(long userId, [FromBody] AssignRolesRequest request, CancellationToken ct)
        {
            var targetIdentity = await _userService.GetByIdWithRolesAsync(userId);
            if (targetIdentity == null)
            {
                return NotFound(new { message = $"Identity {userId} no encontrada." });
            }

            var requesterRoles = User.Claims
                .Where(claim => claim.Type == ClaimTypes.Role)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool requesterIsSuperAdmin = requesterRoles.Contains("superadmin");
            bool requesterIsAdmin = requesterRoles.Contains("admin");

            var targetIdentityRoles = targetIdentity.Memberships
                .Select(membership => membership.Role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var requestedRoles = request.Roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requesterIsAdmin && !requesterIsSuperAdmin)
            {
                var isTryingToAssignPrivilegedRole = requestedRoles.Contains("admin") || requestedRoles.Contains("superadmin");
                var targetIsPrivilegedIdentity = targetIdentityRoles.Contains("admin") || targetIdentityRoles.Contains("superadmin");

                if (isTryingToAssignPrivilegedRole || targetIsPrivilegedIdentity)
                {
                    await _auditService.LogIndependentAsync(new AuditEntry
                    {
                        Action = AuditActions.AssignRolesDenied,
                        EntityType = "Identity",
                        EntityId = targetIdentity.Id.ToString(),
                        NewValues = new { RequestedRoles = requestedRoles.OrderBy(role => role).ToArray() },
                        Success = false,
                        FailureReason = "Política: admin no puede asignar ni modificar roles privilegiados"
                    }, ct);

                    return Forbid();
                }
            }

            var currentUserId = GetCurrentUserId();
            bool targetIsSuperAdmin = targetIdentityRoles.Contains("superadmin");
            bool isRemovingSuperAdmin = targetIsSuperAdmin && !requestedRoles.Contains("superadmin");
            bool isAnotherIdentity = currentUserId != null && currentUserId != targetIdentity.Id;

            if (requesterIsSuperAdmin && isAnotherIdentity && isRemovingSuperAdmin)
            {
                await _auditService.LogIndependentAsync(new AuditEntry
                {
                    Action = AuditActions.AssignRolesDenied,
                    EntityType = "Identity",
                    EntityId = targetIdentity.Id.ToString(),
                    NewValues = new { RequestedRoles = requestedRoles.OrderBy(role => role).ToArray() },
                    Success = false,
                    FailureReason = "Política: no puedes degradar a otro superadmin"
                }, ct);

                return Forbid();
            }

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
                .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == JwtRegisteredClaimNames.Sub /* no-op to keep selection */)
                .Where(claim => claim.Type == ClaimTypes.Role)
                .Select(claim => claim.Value)
                .ToList();

            bool isSuper = requesterRoles.Contains("superadmin");
            bool isAdmin = requesterRoles.Contains("admin");

            var allIdentities = await _userService.GetAllWithRolesAsync();
            var usersDto = new List<UserWithRolesDto>();

            foreach (var identity in allIdentities)
            {
                var identityRoles = identity.Memberships.Select(membership => membership.Role.Name).ToList();

                // Authorization rules
                if (isSuper)
                {
                    // superadmin can see users with roles admin and user
                    if (!identityRoles.Any(role => role == "admin" || role == "user"))
                        continue;
                }
                else if (isAdmin)
                {
                    // admin can see only users with role user
                    if (!identityRoles.Any(role => role == "user"))
                        continue;
                }
                else
                {
                    // non-admins can only see themselves
                    var currentId = GetCurrentUserId();
                    if (currentId == null || currentId != identity.Id)
                        continue;
                }

                usersDto.Add(new UserWithRolesDto
                {
                    Id = identity.Id.ToString(),
                    Email = identity.Email,
                    PhoneNumber = identity.PhoneNumber,
                    Name = identity.Name,
                    Roles = identityRoles
                });
            }

            return Ok(usersDto);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var identity = await _userService.GetByIdWithRolesAsync(id);
            if (identity == null) return NotFound();

            var identityRoles = identity.Memberships.Select(membership => membership.Role.Name).ToList();

            // Determine requester roles
            var requesterRoles = User.Claims
                .Where(claim => claim.Type == ClaimTypes.Role)
                .Select(claim => claim.Value)
                .ToList();

            bool isSuper = requesterRoles.Contains("superadmin");
            bool isAdmin = requesterRoles.Contains("admin");

            if (isSuper)
            {
                if (!identityRoles.Any(role => role == "admin" || role == "user"))
                {
                    await _auditService.LogIndependentAsync(new AuditEntry
                    {
                        Action = AuditActions.UserReadDenied,
                        EntityType = "Identity",
                        EntityId = identity.Id.ToString(),
                        Success = false,
                        FailureReason = "Política: superadmin no puede acceder a este tipo de usuario"
                    });
                    return Forbid();
                }
            }
            else if (isAdmin)
            {
                if (!identityRoles.Any(role => role == "user"))
                {
                    await _auditService.LogIndependentAsync(new AuditEntry
                    {
                        Action = AuditActions.UserReadDenied,
                        EntityType = "Identity",
                        EntityId = identity.Id.ToString(),
                        Success = false,
                        FailureReason = "Política: admin solo puede acceder a usuarios con rol user"
                    });
                    return Forbid();
                }
            }
            else
            {
                var currentId = GetCurrentUserId();
                if (currentId == null || currentId != identity.Id)
                {
                    await _auditService.LogIndependentAsync(new AuditEntry
                    {
                        Action = AuditActions.UserReadDenied,
                        EntityType = "Identity",
                        EntityId = identity.Id.ToString(),
                        Success = false,
                        FailureReason = "Política: solo puedes acceder a tu propio usuario"
                    });
                    return Forbid();
                }
            }

            var userDto = new UserWithRolesDto
            {
                Id = identity.Id.ToString(),
                Email = identity.Email,
                PhoneNumber = identity.PhoneNumber,
                Name = identity.Name,
                Roles = identityRoles
            };

            return Ok(userDto);
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
