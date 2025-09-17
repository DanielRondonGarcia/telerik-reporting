namespace GenReports.Models
{
    /// <summary>
    /// Clase que representa un archivo con URL de descarga temporal para las respuestas optimizadas de la API
    /// </summary>
    public class UFileDownload
    {
        /// <summary>
        /// Nombre del archivo
        /// </summary>
        public string NombreArchivo { get; set; } = string.Empty;

        /// <summary>
        /// URL temporal para descargar el archivo
        /// </summary>
        public string UrlDescarga { get; set; } = string.Empty;

        /// <summary>
        /// Token de descarga único
        /// </summary>
        public string TokenDescarga { get; set; } = string.Empty;

        /// <summary>
        /// Usuario que generó el archivo
        /// </summary>
        public string Usuario { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de generación del archivo
        /// </summary>
        public DateTime FechaGeneracion { get; set; }

        /// <summary>
        /// Fecha de expiración de la URL de descarga
        /// </summary>
        public DateTime FechaExpiracion { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string ContentType { get; set; } = "application/pdf";

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long TamanoBytes { get; set; }

        /// <summary>
        /// Hash MD5 del archivo (para validación de caché)
        /// </summary>
        public string MD5Hash { get; set; } = string.Empty;

        /// <summary>
        /// Indica si el archivo fue encontrado en caché
        /// </summary>
        public bool EncontradoEnCache { get; set; }

        /// <summary>
        /// Tiempo restante hasta la expiración en minutos
        /// </summary>
        public int MinutosHastaExpiracion => (int)(FechaExpiracion - DateTime.UtcNow).TotalMinutes;

        /// <summary>
        /// Detecta automáticamente el tipo de contenido basado en la extensión del archivo
        /// </summary>
        public void DetectContentType()
        {
            if (string.IsNullOrEmpty(NombreArchivo))
                return;

            var extension = Path.GetExtension(NombreArchivo).ToLowerInvariant();
            ContentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}