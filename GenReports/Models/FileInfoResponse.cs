namespace GenReports.Models
{
    /// <summary>
    /// DTO para la respuesta de metadatos de un archivo temporal.
    /// </summary>
    public record FileInfoResponse
    {
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long FileSizeBytes { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string DownloadToken { get; init; } = string.Empty;
    }
}