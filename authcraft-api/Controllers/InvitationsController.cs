using Application.Interfaces;
using Domain.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InvitationsController : ControllerBase
    {
        private readonly IInvitationService _invitationService;
        public InvitationsController(IInvitationService invitationService) => _invitationService = invitationService;

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
                invitation.ExpiresAt
            ));
        }
    }
}
