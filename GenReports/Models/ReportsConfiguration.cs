namespace GenReports.Models
{
    /// <summary>
    /// Configuración para la gestión de reportes
    /// </summary>
    public class ReportsConfiguration
    {
        /// <summary>
        /// Ruta base donde se encuentran las plantillas de reportes
        /// </summary>
        public string BasePath { get; set; } = string.Empty;

        /// <summary>
        /// Directorio temporal para archivos generados
        /// </summary>
        public string TemporaryDirectory { get; set; } = string.Empty;
    }
}