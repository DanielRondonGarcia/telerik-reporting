namespace GenReports.Models
{
    /// <summary>
    /// Información de un archivo temporal en caché
    /// </summary>
    public class TemporaryFileInfo
    {
        /// <summary>
        /// Hash MD5 del contenido del archivo
        /// </summary>
        public string MD5Hash { get; set; } = string.Empty;

        /// <summary>
        /// Ruta del archivo en el sistema de archivos
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Nombre original del archivo
        /// </summary>
        public string OriginalFileName { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora de creación del archivo
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Fecha y hora de expiración del archivo
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Token único para la descarga
        /// </summary>
        public string DownloadToken { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de contenido del archivo
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";
    }
}