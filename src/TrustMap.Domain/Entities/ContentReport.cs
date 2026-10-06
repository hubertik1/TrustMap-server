namespace TrustMap.Domain.Entities;

public sealed class ContentReport
{
    public Guid Id { get; set; }
    public Guid ReporterUserId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid ReviewId { get; set; }
    public string ReviewType { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EmailProcessedAtUtc { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTime NextDeliveryAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
    public List<ReportPhoto> Photos { get; set; } = [];
}

public sealed class ReportPhoto
{
    public Guid Id { get; set; }
    public Guid ContentReportId { get; set; }
    public Guid SourcePhotoId { get; set; }
    public byte[] PreviewJpeg { get; set; } = [];
}
