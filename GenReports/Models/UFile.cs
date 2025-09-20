namespace GenReports.Models
{
    /// <summary>
    /// Clase que representa un archivo para las respuestas de la API
    /// </summary>
    public class UFile
    {
        /// <summary>
        /// Nombre del archivo
        /// </summary>
        public string NombreArchivo { get; set; } = string.Empty;

        /// <summary>
        /// Contenido del archivo en bytes
        /// </summary>
        public byte[]? BytesArchivo { get; set; }

        /// <summary>
        /// Usuario que generó el archivo
        /// </summary>
        public string Usuario { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de generación del archivo
        /// </summary>
        public DateTime FechaGeneracion { get; set; }

        /// <summary>
        /// Tipo MIME del archivo
        /// </summary>
        public string ContentType { get; set; } = "application/pdf";

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long TamanoBytes => BytesArchivo?.Length ?? 0;

        /// <summary>
        /// Detecta automáticamente el tipo de contenido basado en la extensión del archivo
        /// </summary>
        public void DetectContentType()
        {
            ContentType = GetContentTypeFromFileName(NombreArchivo);
        }

        /// <summary>
        /// Método estático helper para obtener el ContentType basado en el nombre del archivo
        /// </summary>
        /// <param name="fileName">Nombre del archivo con extensión</param>
        /// <returns>ContentType MIME apropiado</returns>
        public static string GetContentTypeFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "application/octet-stream";

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".7z" => "application/x-7z-compressed",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}