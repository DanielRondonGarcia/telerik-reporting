namespace GenReports.Models
{
    /// <summary>
    /// Clase que contiene métricas detalladas de rendimiento para la generación de reportes
    /// </summary>
    public class ReportPerformanceMetrics
    {
        /// <summary>
        /// Tiempo total de ejecución en milisegundos
        /// </summary>
        public long TotalExecutionTimeMs { get; set; }

        /// <summary>
        /// Tiempo de generación del reporte consolidado en milisegundos
        /// </summary>
        public long ReportGenerationTimeMs { get; set; }

        /// <summary>
        /// Tiempo de carga de plantilla en milisegundos
        /// </summary>
        public long TemplateLoadTimeMs { get; set; }

        /// <summary>
        /// Tiempo de configuración de datos en milisegundos
        /// </summary>
        public long DataConfigurationTimeMs { get; set; }

        /// <summary>
        /// Tiempo de renderizado en milisegundos
        /// </summary>
        public long RenderingTimeMs { get; set; }

        /// <summary>
        /// Tiempo total del proceso de split en milisegundos
        /// </summary>
        public long SplitTotalTimeMs { get; set; }

        /// <summary>
        /// Tiempo promedio por archivo individual en el split (en milisegundos)
        /// </summary>
        public double SplitAverageTimePerFileMs { get; set; }

        /// <summary>
        /// Número de archivos generados en el split
        /// </summary>
        public int FilesGenerated { get; set; }

        /// <summary>
        /// Número de registros procesados
        /// </summary>
        public int RecordsProcessed { get; set; }

        /// <summary>
        /// Tiempo promedio por registro procesado (en milisegundos)
        /// </summary>
        public double AverageTimePerRecordMs { get; set; }

        /// <summary>
        /// Registros por segundo (r/s) calculados
        /// </summary>
        public double RecordsPerSecond { get; set; }

        /// <summary>
        /// Tiempo de compresión del archivo final en milisegundos
        /// </summary>
        public long CompressionTimeMs { get; set; }

        /// <summary>
        /// Tamaño del archivo consolidado en bytes
        /// </summary>
        public long ConsolidatedFileSizeBytes { get; set; }

        /// <summary>
        /// Tamaño del archivo final comprimido en bytes
        /// </summary>
        public long FinalFileSizeBytes { get; set; }

        /// <summary>
        /// Ratio de compresión (tamaño final / tamaño consolidado)
        /// </summary>
        public double CompressionRatio => ConsolidatedFileSizeBytes > 0 
            ? (double)FinalFileSizeBytes / ConsolidatedFileSizeBytes 
            : 0;

        /// <summary>
        /// Detalles adicionales del proceso
        /// </summary>
        public Dictionary<string, object> AdditionalDetails { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Indica si se utilizó caché para obtener el resultado
        /// </summary>
        public bool UsedCache { get; set; }

        /// <summary>
        /// Tiempo de búsqueda en caché en milisegundos
        /// </summary>
        public long CacheLookupTimeMs { get; set; }

        /// <summary>
        /// Calcula automáticamente los promedios basados en los datos disponibles
        /// </summary>
        public void CalculateAverages()
        {
            if (RecordsProcessed > 0)
            {
                // Prioriza el tiempo de generación si está disponible, si no usa el total
                var baseMs = ReportGenerationTimeMs > 0 ? ReportGenerationTimeMs : TotalExecutionTimeMs;
                if (baseMs > 0)
                {
                    AverageTimePerRecordMs = (double)baseMs / RecordsProcessed;
                    RecordsPerSecond = RecordsProcessed / (baseMs / 1000.0);
                }
            }

            if (FilesGenerated > 0 && SplitTotalTimeMs > 0)
            {
                SplitAverageTimePerFileMs = (double)SplitTotalTimeMs / FilesGenerated;
            }
        }

        /// <summary>
        /// Obtiene un resumen legible de las métricas
        /// </summary>
        public string GetSummary()
        {
            return $"Total: {TotalExecutionTimeMs}ms | " +
                   $"Generación: {ReportGenerationTimeMs}ms ({AverageTimePerRecordMs:F2}ms/registro, {RecordsPerSecond:F2} r/s) | " +
                   $"Split: {SplitTotalTimeMs}ms ({SplitAverageTimePerFileMs:F2}ms/archivo) | " +
                   $"Archivos: {FilesGenerated} | Registros: {RecordsProcessed}";
        }
    }
}