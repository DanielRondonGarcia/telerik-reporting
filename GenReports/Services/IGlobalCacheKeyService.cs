namespace GenReports.Services
{
    /// <summary>
    /// Servicio para generar claves de caché globales basadas en contenido
    /// </summary>
    public interface IGlobalCacheKeyService
    {
        /// <summary>
        /// Genera una clave de caché global basada únicamente en el contenido JSON
        /// </summary>
        /// <param name="jsonContent">Contenido JSON a procesar</param>
        /// <returns>Clave de caché global</returns>
        string GenerateContentBasedCacheKey(string jsonContent);

        /// <summary>
        /// Genera una clave de caché que incluye el tipo de reporte para diferenciar formatos de salida
        /// </summary>
        /// <param name="jsonContent">Contenido JSON a procesar</param>
        /// <param name="reportType">Tipo de reporte (para diferenciar PDF vs Excel, etc.)</param>
        /// <returns>Clave de caché específica por tipo de reporte</returns>
        string GenerateReportTypeCacheKey(string jsonContent, string reportType);

        /// <summary>
        /// Normaliza el contenido JSON para asegurar consistencia en el hash
        /// </summary>
        /// <param name="jsonContent">Contenido JSON original</param>
        /// <returns>JSON normalizado</returns>
        string NormalizeJsonContent(string jsonContent);
    }
}