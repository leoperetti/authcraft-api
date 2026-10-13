using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities;

namespace Infrastructure.Data.Configurations
{
    public class LocalAccountConfiguration : IEntityTypeConfiguration<LocalAccount>
    {
        public void Configure(EntityTypeBuilder<LocalAccount> builder)
        {
            builder.ToTable("local_accounts");

            builder.HasKey(l => l.Id).HasName("PK_local_accounts");
            builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(localAccount => localAccount.IdentityId).HasColumnName("identity_id");

            builder.Property(l => l.PasswordHash).HasColumnName("password_hash");
            builder.Property(l => l.Salt).HasColumnName("salt");

            builder.Property(localAccount => localAccount.Algorithm)
                .HasMaxLength(400)
                .HasColumnName("algorithm");

            builder.Property(l => l.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(l => l.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(localAccount => localAccount.IdentityId).IsUnique().HasDatabaseName("UQ_local_accounts_identity");
            builder.HasIndex(localAccount => localAccount.IdentityId).HasDatabaseName("IX_local_accounts_identity");

            builder.HasOne(localAccount => localAccount.Identity)
                .WithMany()
                .HasForeignKey(localAccount => localAccount.IdentityId)
                .HasConstraintName("FK_local_accounts_identity")
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
