using System.Net.Mail;
using System.Security.Cryptography;
using Application.Interfaces;
using Domain.Dto;
using Domain.Entities;
using Domain.Request;
using Domain.Response;
using Infrastructure.Repositories;
using DomainNotFoundException = Domain.Exceptions.NotFoundException;

namespace Application.Services
{
    public class InvitationService : IInvitationService
    {
        private const int InvitationValidityDays = 7;
        private const int MaxEmailLength = 200;

        private readonly IInvitationRepository _invitationRepository;
        private readonly ITenantRepository _tenantRepository;
        private readonly IEmailService _emailService;

        public InvitationService(IInvitationRepository invitationRepository, ITenantRepository tenantRepository, IEmailService emailService)
        {
            _invitationRepository = invitationRepository;
            _tenantRepository = tenantRepository;
            _emailService = emailService;
        }

        public async Task<ResolvedInvitationDto?> ResolveByCodeAsync(string invitationCode, CancellationToken cancellationToken = default)
        {
            return await _invitationRepository.GetByCodeAsync(invitationCode, cancellationToken);
        }

        public async Task<CreateInvitationResponse> CreateAsync(string tenantClientId, string email, CancellationToken cancellationToken = default)
        {
            return await CreateAsync(tenantClientId, email, null, cancellationToken);
        }

        public async Task<CreateInvitationResponse> CreateAsync(
            string tenantClientId,
            string email,
            StudentInvitationProfileRequest? studentProfile,
            CancellationToken cancellationToken = default)
        {
            var normalizedEmail = NormalizeEmail(email);
            var tenant = await GetTenantAsync(tenantClientId, cancellationToken);
            var profile = ValidateStudentProfile(tenant, studentProfile);

            if (await _invitationRepository.HasPendingAsync(tenant.Id, normalizedEmail, cancellationToken))
            {
                throw new PendingInvitationException();
            }

            return await IssueInvitationAsync(tenant, normalizedEmail, profile, cancellationToken);
        }

        public async Task<IReadOnlyList<InvitationDto>> ListPendingAsync(string tenantClientId, CancellationToken cancellationToken = default)
        {
            var tenant = await GetTenantAsync(tenantClientId, cancellationToken);
            return await _invitationRepository.ListPendingAsync(tenant.Id, cancellationToken);
        }

        public async Task<CreateInvitationResponse> ResendAsync(string tenantClientId, long invitationId, CancellationToken cancellationToken = default)
        {
            var tenant = await GetTenantAsync(tenantClientId, cancellationToken);
            var previous = await _invitationRepository.GetUnusedByIdAsync(tenant.Id, invitationId, cancellationToken)
                ?? throw new DomainNotFoundException("Invitación no encontrada.");

            if (previous.Purpose == "gym_student")
            {
                var existingProfile = new StudentInvitationProfileRequest
                {
                    StudentId = previous.StudentId ?? 0,
                    FirstName = previous.FirstName,
                    LastName = previous.LastName,
                    FullName = previous.FullName ?? string.Empty,
                    PhoneNumber = previous.PhoneNumber,
                    Dni = previous.Dni
                };
                return await ResendAsync(tenantClientId, invitationId, previous.Email, existingProfile, cancellationToken);
            }

            previous.Used = true;
            return await IssueInvitationAsync(tenant, previous.Email, null, cancellationToken);
        }

        public async Task<CreateInvitationResponse> ResendAsync(
            string tenantClientId,
            long invitationId,
            string email,
            StudentInvitationProfileRequest studentProfile,
            CancellationToken cancellationToken = default)
        {
            var tenant = await GetTenantAsync(tenantClientId, cancellationToken);
            var previous = await _invitationRepository.GetUnusedByIdAsync(tenant.Id, invitationId, cancellationToken)
                ?? throw new DomainNotFoundException("Invitación no encontrada.");
            if (previous.Purpose != "gym_student")
            {
                throw new InvalidStudentInvitationProfileException();
            }

            var normalizedEmail = NormalizeEmail(email);
            var profile = ValidateStudentProfile(tenant, studentProfile);
            if (profile is null || profile.StudentId != previous.StudentId)
            {
                throw new InvalidStudentInvitationProfileException();
            }

            previous.Used = true;
            return await IssueInvitationAsync(tenant, normalizedEmail, profile, cancellationToken);
        }

        public async Task<AcceptedInvitationProfileResponse?> GetAcceptedProfileAsync(
            string tenantClientId,
            long invitationId,
            long identityId,
            CancellationToken cancellationToken = default)
        {
            var tenant = await GetTenantAsync(tenantClientId, cancellationToken);
            var invitation = await _invitationRepository.GetAcceptedByIdAsync(
                tenant.Id,
                invitationId,
                identityId,
                cancellationToken);
            if (invitation?.StudentId is null || invitation.AcceptedAt is null)
            {
                return null;
            }

            return new AcceptedInvitationProfileResponse
            {
                InvitationId = invitation.Id,
                StudentId = invitation.StudentId.Value,
                IdentityId = identityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AcceptedAt = invitation.AcceptedAt.Value,
                Prefill = new AcceptedInvitationPrefillResponse
                {
                    FirstName = invitation.FirstName,
                    LastName = invitation.LastName,
                    FullName = invitation.FullName ?? string.Empty,
                    PhoneNumber = invitation.PhoneNumber,
                    Dni = invitation.Dni
                },
                Registered = new AcceptedInvitationRegisteredResponse
                {
                    FirstName = invitation.RegisteredFirstName ?? string.Empty,
                    LastName = invitation.RegisteredLastName ?? string.Empty,
                    PhoneNumber = invitation.RegisteredPhoneNumber,
                    Dni = invitation.RegisteredDni ?? string.Empty
                }
            };
        }

        private async Task<Tenant> GetTenantAsync(string tenantClientId, CancellationToken cancellationToken)
        {
            return await _tenantRepository.GetByClientIdAsync(tenantClientId, cancellationToken)
                ?? throw new DomainNotFoundException("Tenant no encontrado.");
        }

        private async Task<CreateInvitationResponse> IssueInvitationAsync(
            Tenant tenant,
            string email,
            StudentInvitationProfileRequest? studentProfile,
            CancellationToken cancellationToken)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            var invitation = new Invitation
            {
                TenantId = tenant.Id,
                Email = email,
                Code = code,
                CreatedAt = now,
                ExpiresAt = now.AddDays(InvitationValidityDays),
                Used = false,
                Purpose = studentProfile is null ? null : "gym_student",
                StudentId = studentProfile?.StudentId,
                FirstName = studentProfile?.FirstName,
                LastName = studentProfile?.LastName,
                FullName = studentProfile?.FullName,
                PhoneNumber = studentProfile?.PhoneNumber,
                Dni = studentProfile?.Dni
            };

            await _invitationRepository.AddAsync(invitation, cancellationToken);

            var path = $"/signup?invitationCode={code}";
            var invitationUrl = string.IsNullOrWhiteSpace(tenant.PrimaryDomain)
                ? path
                : tenant.PrimaryDomain.TrimEnd('/') + path;

            var subject = "Invitación para crear tu cuenta";
            var body = $"Te invitaron a registrarte en {tenant.Name}. Usá el siguiente enlace para completar tu registro: <a href=\"{invitationUrl}\">{invitationUrl}</a>";
            _emailService.SendEmail(email, subject, body);

            return new CreateInvitationResponse(invitation.Id, invitation.ExpiresAt.Value);
        }

        private static string NormalizeEmail(string? email)
        {
            var trimmed = email?.Trim();
            if (string.IsNullOrEmpty(trimmed)
                || trimmed.Length > MaxEmailLength
                || !MailAddress.TryCreate(trimmed, out var parsed)
                || !string.Equals(parsed.Address, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidInvitationEmailException();
            }

            return trimmed;
        }

        private static StudentInvitationProfileRequest? ValidateStudentProfile(Tenant tenant, StudentInvitationProfileRequest? profile)
        {
            if (profile is null)
            {
                return null;
            }

            if (!string.Equals(tenant.ClientId, "cekide-app", StringComparison.Ordinal)
                || profile.StudentId <= 0
                || string.IsNullOrWhiteSpace(profile.FullName)
                || profile.FullName.Trim().Length > 150
                || (profile.FirstName is null) != (profile.LastName is null)
                || profile.FirstName is not null && string.IsNullOrWhiteSpace(profile.FirstName)
                || profile.LastName is not null && string.IsNullOrWhiteSpace(profile.LastName)
                || profile.FirstName?.Trim().Length > 150
                || profile.LastName?.Trim().Length > 150
                || profile.PhoneNumber?.Trim().Length > 30
                || profile.FirstName is not null
                    && string.Join(" ", profile.FirstName.Trim(), profile.LastName!.Trim()).Length > 150
                || profile.Dni is not null && !IsValidDni(profile.Dni))
            {
                throw new InvalidStudentInvitationProfileException();
            }

            return new StudentInvitationProfileRequest
            {
                StudentId = profile.StudentId,
                FirstName = profile.FirstName?.Trim(),
                LastName = profile.LastName?.Trim(),
                FullName = profile.FullName.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(profile.PhoneNumber) ? null : profile.PhoneNumber.Trim(),
                Dni = profile.Dni?.Trim()
            };
        }

        private static bool IsValidDni(string dni)
        {
            if (string.IsNullOrWhiteSpace(dni)
                || !System.Text.RegularExpressions.Regex.IsMatch(dni, "^[0-9. ]+$"))
            {
                return false;
            }

            var normalized = dni.Replace(".", string.Empty).Replace(" ", string.Empty);
            return System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[0-9]{7,8}$");
        }
    }
}
