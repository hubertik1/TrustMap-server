using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("places", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_places_source_type",
                EnumCheckConstraintSql.StringNames<PlaceSourceType>("SourceType"));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NormalizedAddress).HasMaxLength(300).IsRequired();
        builder.Property(x => x.City).HasMaxLength(120);
        builder.Property(x => x.CountryCode).HasMaxLength(2);
        builder.Property(x => x.Provider).HasMaxLength(40);
        builder.Property(x => x.ProviderPlaceId).HasMaxLength(200);
        builder.Property(x => x.SourceType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(x => x.IsPubliclyListed).IsRequired();
        builder.Property(x => x.CustomDisplayName).HasMaxLength(200);
        builder.Property(x => x.NormalizedCustomDisplayName).HasMaxLength(200);
        builder.Property(x => x.IdentityFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.IdentityFingerprint);
        builder.HasIndex(x => new { x.Provider, x.ProviderPlaceId }).IsUnique();
        builder.HasIndex(x => x.SourceType);
        builder.HasIndex(x => x.IsPubliclyListed);
        builder.HasIndex(x => x.NormalizedName);
        builder.HasIndex(x => x.NormalizedCustomDisplayName);
        builder.HasIndex(x => new { x.Latitude, x.Longitude });
        builder.HasIndex(x => x.CreatedByUserId);
        builder.HasIndex(x => new { x.CreatedByUserId, x.IdentityFingerprint }).IsUnique();

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CustomDisplayNameSetByUser)
            .WithMany()
            .HasForeignKey(x => x.CustomDisplayNameSetByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
