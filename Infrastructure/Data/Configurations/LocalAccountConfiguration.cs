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

            builder.Property(l => l.UserId).HasColumnName("user_id");

            builder.Property(l => l.PasswordHash).HasColumnName("password_hash");
            builder.Property(l => l.Salt).HasColumnName("salt");

            builder.Property(l => l.Algorithm)
                .HasMaxLength(200)
                .HasColumnName("algorithm");

            builder.Property(l => l.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(l => l.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(l => l.UserId).IsUnique().HasDatabaseName("UQ_local_accounts_userid");

            builder.HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .HasConstraintName("FK_local_accounts_user")
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
