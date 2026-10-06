using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities;

namespace Infrastructure.Data.Configurations
{
    public class OAuthAccountConfiguration : IEntityTypeConfiguration<OAuthAccount>
    {
        public void Configure(EntityTypeBuilder<OAuthAccount> builder)
        {
            builder.ToTable("oauth_accounts");

            builder.HasKey(o => o.Id).HasName("PK_oauth_accounts");
            builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(o => o.UserId).HasColumnName("user_id");
            builder.Property(o => o.ProviderId).HasColumnName("provider_id");

            builder.Property(o => o.ProviderUserId)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnName("provider_user_id");

            builder.Property(o => o.Email)
                .IsRequired()
                .HasMaxLength(320)
                .HasColumnName("email");

            builder.Property(o => o.EmailVerified)
                .IsRequired()
                .HasColumnName("email_verified")
                .HasDefaultValue(false);

            builder.Property(o => o.Hd)
                .HasMaxLength(253)
                .HasColumnName("hd");

            builder.Property(o => o.RefreshToken).HasColumnName("refresh_token");
            builder.Property(o => o.AccessToken).HasColumnName("access_token");

            builder.Property(o => o.ExpiresAt).HasColumnName("expires_at");
            builder.Property(o => o.LastLoginAt).HasColumnName("last_login_at");

            builder.Property(o => o.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(o => o.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(o => o.UserId).HasDatabaseName("IX_oauth_accounts_user");
            builder.HasIndex(o => o.ProviderUserId).HasDatabaseName("IX_oauth_accounts_provideruserid");

            builder.HasIndex(o => new { o.ProviderId, o.ProviderUserId })
                .IsUnique()
                .HasDatabaseName("UQ_oauth_accounts_provider_provideruserid");

            builder.HasOne(o => o.User)
                .WithMany(u => u.OAuthAccounts)
                .HasForeignKey(o => o.UserId)
                .HasConstraintName("FK_oauth_accounts_user")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(o => o.Provider)
                .WithMany(p => p.OAuthAccounts)
                .HasForeignKey(o => o.ProviderId)
                .HasConstraintName("FK_oauth_accounts_provider")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
