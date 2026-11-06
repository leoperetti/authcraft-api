using Application.Interfaces;
using Application.Services;
using Domain.Dto;
using Domain.Exceptions;
using Domain.Request;
using Domain.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InvitationsController : ControllerBase
    {
        private const string InviterRoles = "owner,teacher";

        private readonly IInvitationService _invitationService;
        public InvitationsController(IInvitationService invitationService) => _invitationService = invitationService;

        [HttpPost]
        [Authorize(Roles = InviterRoles)]
        [ProducesResponseType(typeof(CreateInvitationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request, CancellationToken ct)
        {
            var tenantClientId = User.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenantClientId))
                return Unauthorized("El token no contiene el claim 'tenant' (client_id)");

            try
            {
                var created = await _invitationService.CreateAsync(tenantClientId, request.Email, request.StudentProfile, ct);
                return StatusCode(StatusCodes.Status201Created, created);
            }
            catch (InvalidInvitationEmailException ex)
            {
                return BadRequest(new ProblemDetails { Title = "Email inválido", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (InvalidStudentInvitationProfileException ex)
            {
                return BadRequest(new ProblemDetails { Title = "Perfil de alumno inválido", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (PendingInvitationException ex)
            {
                return Conflict(new ProblemDetails { Title = "Invitación pendiente", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet]
        [Authorize(Roles = InviterRoles)]
        [ProducesResponseType(typeof(IReadOnlyList<InvitationDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ListPending(CancellationToken ct)
        {
            var tenantClientId = User.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenantClientId))
                return Unauthorized("El token no contiene el claim 'tenant' (client_id)");

            try
            {
                return Ok(await _invitationService.ListPendingAsync(tenantClientId, ct));
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost("{id:long}/resend")]
        [Authorize(Roles = InviterRoles)]
        [ProducesResponseType(typeof(CreateInvitationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Resend(
            long id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResendInvitationRequest? request,
            CancellationToken ct)
        {
            var tenantClientId = User.FindFirstValue("tenant");
            if (string.IsNullOrWhiteSpace(tenantClientId))
                return Unauthorized("El token no contiene el claim 'tenant' (client_id)");

            try
            {
                if (request is null)
                {
                    var created = await _invitationService.ResendAsync(tenantClientId, id, ct);
                    return StatusCode(StatusCodes.Status201Created, created);
                }

                if (request.StudentProfile is null)
                {
                    throw new InvalidStudentInvitationProfileException();
                }

                var resent = await _invitationService.ResendAsync(tenantClientId, id, request.Email, request.StudentProfile, ct);
                return StatusCode(StatusCodes.Status201Created, resent);
            }
            catch (InvalidInvitationEmailException ex)
            {
                return BadRequest(new ProblemDetails { Title = "Email inválido", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (InvalidStudentInvitationProfileException ex)
            {
                return BadRequest(new ProblemDetails { Title = "Perfil de alumno inválido", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{id:long}/accepted-profile")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(AcceptedInvitationProfileResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAcceptedProfile(long id, CancellationToken ct)
        {
            var tenantClientId = User.FindFirstValue("tenant");
            var identityIdRaw = User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(tenantClientId)
                || !long.TryParse(identityIdRaw, out var identityId)
                || identityId <= 0)
            {
                return Unauthorized();
            }

            try
            {
                var profile = await _invitationService.GetAcceptedProfileAsync(tenantClientId, id, identityId, ct);
                return profile is null ? NotFound() : Ok(profile);
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{invitationCode}/resolve")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(ResolveInvitationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status410Gone)]
        public async Task<IActionResult> ResolveInvitation(string invitationCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(invitationCode) || invitationCode.Length > 128)
                return BadRequest(new { error = "invalid_code" });

            var invitation = await _invitationService.ResolveByCodeAsync(invitationCode, ct);

            if (invitation is null || invitation.Used)
                return NotFound(new { error = "invitation_not_found_or_used" });

            if (invitation.ExpiresAt is not null && invitation.ExpiresAt < DateTimeOffset.UtcNow)
                return StatusCode(StatusCodes.Status410Gone, new { error = "invitation_expired" });

            return Ok(new ResolveInvitationResponse(
                invitation.Email,
                invitation.TenantName,
                invitation.TenantSlug,
                invitation.ExpiresAt,
                invitation.StudentProfile,
                invitation.DniRequired
            ));
        }
    }
}
