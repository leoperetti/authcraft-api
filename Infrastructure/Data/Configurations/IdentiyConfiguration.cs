using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities;

namespace Infrastructure.Data.Configurations
{
    public class IdentiyConfiguration : IEntityTypeConfiguration<Identity>
    {
        public void Configure(EntityTypeBuilder<Identity> builder)
        {
            builder.ToTable("identities");

            builder.HasKey(user => user.Id).HasName("PK_identities");
            builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(user => user.Email)
                .IsRequired()
                .HasMaxLength(320)
                .HasColumnName("email");

            builder.Property(user => user.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phone_number");

            builder.Property(user => user.Name)
                .HasMaxLength(200)
                .HasColumnName("name");

            builder.Property(user => user.Picture)
                .HasMaxLength(1000)
                .HasColumnName("picture");

            builder.Property(user => user.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(user => user.Email).HasDatabaseName("IX_identities_email");
        }
    }
}
