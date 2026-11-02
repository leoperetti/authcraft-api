using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    internal class PasswordResetTokenConfiguration: IEntityTypeConfiguration<PasswordResetToken>
    {
        public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
        {
            // Tabla
            builder.ToTable("password_reset_tokens");

            // PK
            builder.HasKey(a => a.Id);

            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            builder.Property(a => a.IdentityId)
                .HasColumnName("identity_id")
                .IsRequired();

            builder.Property(a => a.Code)
                .HasColumnName("code")
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(a => a.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(a => a.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            builder.Property(a => a.Used)
                .HasColumnName("used");

            // Relaciones
            builder.HasOne(a => a.Identity)
                .WithMany(a => a.PasswordResetTokens)
                .HasForeignKey(a => a.IdentityId)
                .OnDelete(DeleteBehavior.Cascade);

            //Indexes
            builder.HasIndex(x => x.Code).IsUnique();
            builder.HasIndex(a => a.IdentityId)
                .HasDatabaseName("IX_prt_identity");
        }
    }
}
