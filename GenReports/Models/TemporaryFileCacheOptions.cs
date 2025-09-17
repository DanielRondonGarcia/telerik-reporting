namespace GenReports.Models
{
    /// <summary>
    /// Configuración para el caché de archivos temporales
    /// </summary>
    public class TemporaryFileCacheOptions
    {
        /// <summary>
        /// Directorio donde se almacenan los archivos temporales
        /// </summary>
        public string CacheDirectory { get; set; } = "temp_downloads";

        /// <summary>
        /// Tiempo de expiración en minutos para los archivos temporales
        /// </summary>
        public int ExpirationTimeMinutes { get; set; } = 60;

        /// <summary>
        /// Intervalo en minutos para la limpieza automática de archivos expirados
        /// </summary>
        public int CleanupIntervalMinutes { get; set; } = 30;

        /// <summary>
        /// Tamaño máximo del caché en GB
        /// </summary>
        public double MaxCacheSizeGB { get; set; } = 5.0;

        /// <summary>
        /// Habilitar validación MD5 para evitar duplicados
        /// </summary>
        public bool EnableMD5Validation { get; set; } = true;
    }
}