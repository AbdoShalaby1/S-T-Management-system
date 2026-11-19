using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(100);
        builder.Property(setting => setting.Value).IsRequired();
    }
}
