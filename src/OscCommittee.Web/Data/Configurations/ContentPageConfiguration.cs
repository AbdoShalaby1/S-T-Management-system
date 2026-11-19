using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    /// <summary>Named so the list index can be asserted by the model tests.</summary>
    public const string ListIndex = "IX_ContentPages_IsPublished_Title";

    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        builder.Property(page => page.Title).IsRequired().HasMaxLength(160);
        builder.Property(page => page.BodyMarkdown).IsRequired();

        // The member list reads published pages title-ordered.
        builder.HasIndex(page => new { page.IsPublished, page.Title })
            .HasDatabaseName(ListIndex);

        builder.HasOne(page => page.CreatedBy)
            .WithMany()
            .HasForeignKey(page => page.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Two SET NULL paths to AspNetUsers make SQL Server reject the table (multiple cascade
        // paths), so the updater link is NO ACTION. Accounts are deactivated, never deleted.
        builder.HasOne(page => page.UpdatedBy)
            .WithMany()
            .HasForeignKey(page => page.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}