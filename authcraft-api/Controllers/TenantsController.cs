using Application.Interfaces;
using Domain.Dto;
using Domain.Request;
using Domain.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "SuperadminOnly")]
    public class TenantsController : ControllerBase
    {
        private readonly ITenantService _tenantService;

        public TenantsController(ITenantService tenantService)
        {
            _tenantService = tenantService;
        }

        // GET api/tenants
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<TenantDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<TenantDto>>> List(CancellationToken ct)
            => Ok(await _tenantService.ListAsync(ct));

        // GET api/tenants/{id}
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantDto>> Get(long id, CancellationToken ct)
        {
            var dto = await _tenantService.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        // POST api/tenants
        [HttpPost]
        [ProducesResponseType(typeof(CreateTenantResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<CreateTenantResponse>> Create(
            [FromBody] CreateTenantRequest req,
            CancellationToken ct)
        {
            try
            {
                var res = await _tenantService.CreateAsync(req, ct);
                return CreatedAtAction(nameof(Get), new { id = res.Tenant.Id }, res);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("UQ_tenants_slug") == true
                   || ex.InnerException?.Message.Contains("IX_tenants_client_id") == true)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Tenant duplicado",
                    Detail = "Ya existe un tenant con ese slug o client_id.",
                    Status = StatusCodes.Status409Conflict
                });
            }
        }

        // PUT api/tenants/{id}
        [HttpPut("{id:long}")]
        [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TenantDto>> Update(
            long id,
            [FromBody] UpdateTenantRequest req,
            CancellationToken ct)
        {
            var dto = await _tenantService.UpdateAsync(id, req, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        // PATCH api/tenants/{id}/status
        [HttpPatch("{id:long}/status")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateStatus(
            long id,
            [FromBody] UpdateTenantStatusRequest req,
            CancellationToken ct)
        {
            var ok = await _tenantService.UpdateStatusAsync(id, req.Status, ct);
            return ok ? NoContent() : NotFound();
        }

        // DELETE api/tenants/{id}
        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            var ok = await _tenantService.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
    }
}
