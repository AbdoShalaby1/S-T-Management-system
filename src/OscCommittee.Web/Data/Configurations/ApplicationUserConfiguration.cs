using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(user => user.LeetCodeHandle).HasMaxLength(64);
        builder.Property(user => user.CodeforcesHandle).HasMaxLength(64);

        builder.Property(user => user.Status)
            .HasConversion<int>()
            .IsRequired();

        // Registration must not create two accounts for one mailbox, ignoring case.
        // Identity already owns the EmailIndex; make it unique and tolerate null emails.
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");

        builder.HasIndex(user => user.Status);
    }
}
