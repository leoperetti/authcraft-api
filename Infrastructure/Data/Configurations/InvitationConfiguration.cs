using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
    {
        public void Configure(EntityTypeBuilder<Invitation> builder)
        {
            builder.ToTable("invitations", tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_invitations_purpose", "[purpose] IS NULL OR [purpose] = 'gym_student'");
                tableBuilder.HasCheckConstraint("CK_invitations_student_profile", "[purpose] IS NULL OR ([student_id] > 0 AND [full_name] IS NOT NULL AND LEN([full_name]) > 0 AND (([first_name] IS NULL AND [last_name] IS NULL) OR ([first_name] IS NOT NULL AND [last_name] IS NOT NULL)))");
                tableBuilder.HasCheckConstraint("CK_invitations_student_dni", "[dni] IS NULL OR (LEN(REPLACE(REPLACE([dni], N'.', N''), N' ', N'')) IN (7,8) AND [dni] NOT LIKE N'%[^0-9. ]%')");
                tableBuilder.HasCheckConstraint("CK_invitations_registered_dni", "[registered_dni] IS NULL OR (LEN(REPLACE(REPLACE([registered_dni], N'.', N''), N' ', N'')) IN (7,8) AND [registered_dni] NOT LIKE N'%[^0-9. ]%')");
            });

            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
            builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
            builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
            builder.Property(i => i.Used).HasColumnName("used").IsRequired();
            builder.Property(i => i.Purpose).HasColumnName("purpose").HasMaxLength(32);
            builder.Property(i => i.StudentId).HasColumnName("student_id");
            builder.Property(i => i.FirstName).HasColumnName("first_name").HasMaxLength(150);
            builder.Property(i => i.LastName).HasColumnName("last_name").HasMaxLength(150);
            builder.Property(i => i.FullName).HasColumnName("full_name").HasMaxLength(150);
            builder.Property(i => i.PhoneNumber).HasColumnName("phone_number").HasMaxLength(30);
            builder.Property(i => i.Dni).HasColumnName("dni").HasMaxLength(16);
            builder.Property(i => i.AcceptedIdentityId).HasColumnName("accepted_identity_id");
            builder.Property(i => i.AcceptedAt).HasColumnName("accepted_at");
            builder.Property(i => i.RegisteredFirstName).HasColumnName("registered_first_name").HasMaxLength(150);
            builder.Property(i => i.RegisteredLastName).HasColumnName("registered_last_name").HasMaxLength(150);
            builder.Property(i => i.RegisteredPhoneNumber).HasColumnName("registered_phone_number").HasMaxLength(30);
            builder.Property(i => i.RegisteredDni).HasColumnName("registered_dni").HasMaxLength(16);
            builder.Property(i => i.RowVersion).HasColumnName("row_version").IsRowVersion();

            builder.HasOne(i => i.Tenant)
                .WithMany(t => t.Invitations)
                .HasForeignKey(i => i.TenantId)
                .HasConstraintName("FK_invitations_tenants")
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(i => i.AcceptedIdentity)
                .WithMany()
                .HasForeignKey(i => i.AcceptedIdentityId)
                .HasConstraintName("FK_invitations_accepted_identity")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
