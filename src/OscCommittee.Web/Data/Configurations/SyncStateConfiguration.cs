using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> builder)
    {
        builder.HasKey(state => new { state.UserId, state.Platform });

        builder.Property(state => state.Platform)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(state => state.LastError).HasMaxLength(512);

        builder.HasOne(state => state.User)
            .WithMany()
            .HasForeignKey(state => state.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
