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

        /// <summary>
        /// Tamaño de batch para el modo consolidado + split. Si el total de registros supera este valor,
        /// se procesará en lotes de este tamaño para reducir uso de memoria y evitar errores.
        /// </summary>
        public int ConsolidatedSplitBatchSize { get; set; } = 25000;

        /// <summary>
        /// Umbral de registros para activar el modo de escritura a disco en el split consolidado.
        /// Si el total supera este número, se divide a disco y se comprime desde un directorio.
        /// </summary>
        public int ConsolidatedSplitDiskThreshold { get; set; } = 10000;
    }
}