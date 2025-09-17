using GenReports.Models;

namespace GenReports.Services
{
    /// <summary>
    /// Interfaz para el servicio de caché de archivos temporales
    /// </summary>
    public interface ITemporaryFileCacheService
    {
        /// <summary>
        /// Almacena un archivo en el caché temporal y devuelve la información del archivo
        /// </summary>
        /// <param name="fileBytes">Contenido del archivo</param>
        /// <param name="originalFileName">Nombre original del archivo</param>
        /// <param name="contentType">Tipo de contenido del archivo</param>
        /// <returns>Información del archivo temporal</returns>
        Task<TemporaryFileInfo> StoreFileAsync(byte[] fileBytes, string originalFileName, string contentType = "application/octet-stream");

        /// <summary>
        /// Almacena un archivo en el caché temporal con un hash MD5 personalizado
        /// </summary>
        /// <param name="fileBytes">Contenido del archivo</param>
        /// <param name="originalFileName">Nombre original del archivo</param>
        /// <param name="contentType">Tipo de contenido del archivo</param>
        /// <param name="customMD5Hash">Hash MD5 personalizado para identificar el archivo</param>
        /// <returns>Información del archivo temporal</returns>
        Task<TemporaryFileInfo> StoreFileAsync(byte[] fileBytes, string originalFileName, string contentType, string customMD5Hash);

        /// <summary>
        /// Obtiene la información de un archivo temporal por su token de descarga
        /// </summary>
        /// <param name="downloadToken">Token de descarga</param>
        /// <returns>Información del archivo temporal o null si no existe</returns>
        Task<TemporaryFileInfo?> GetFileInfoAsync(string downloadToken);

        /// <summary>
        /// Obtiene el contenido de un archivo temporal por su token de descarga
        /// </summary>
        /// <param name="downloadToken">Token de descarga</param>
        /// <returns>Contenido del archivo o null si no existe</returns>
        Task<byte[]?> GetFileContentAsync(string downloadToken);

        /// <summary>
        /// Verifica si existe un archivo con el mismo hash MD5
        /// </summary>
        /// <param name="md5Hash">Hash MD5 a verificar</param>
        /// <returns>Información del archivo si existe, null en caso contrario</returns>
        Task<TemporaryFileInfo?> FindByMD5HashAsync(string md5Hash);

        /// <summary>
        /// Genera una URL de descarga temporal para un token
        /// </summary>
        /// <param name="downloadToken">Token de descarga</param>
        /// <param name="baseUrl">URL base del servidor</param>
        /// <returns>URL de descarga completa</returns>
        string GenerateDownloadUrl(string downloadToken, string baseUrl);

        /// <summary>
        /// Valida el hash MD5 de un archivo
        /// </summary>
        /// <param name="filePath">Ruta del archivo</param>
        /// <param name="expectedHash">Hash MD5 esperado</param>
        /// <returns>True si el hash coincide</returns>
        Task<bool> ValidateMD5HashAsync(string filePath, string expectedHash);

        /// <summary>
        /// Limpia archivos expirados del caché temporal
        /// </summary>
        /// <returns>Resultado de la operación de limpieza</returns>
        Task<CleanupResult> CleanupExpiredFilesAsync();
    }
}