using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
    {
        public void Configure(EntityTypeBuilder<Invitation> builder)
        {
            builder.ToTable("invitations");

            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
            builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
            builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(i => i.ExpiresAt).HasColumnName("expires_at");
            builder.Property(i => i.Used).HasColumnName("used").IsRequired();

            builder.HasOne(i => i.Tenant)
                .WithMany(t => t.Invitations)
                .HasForeignKey(i => i.TenantId)
                .HasConstraintName("FK_invitations_tenants")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
