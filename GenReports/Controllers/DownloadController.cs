using GenReports.Services;
using Microsoft.AspNetCore.Mvc;

namespace GenReports.Controllers
{
    /// <summary>
    /// Controlador para manejar descargas de archivos temporales
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly ITemporaryFileCacheService _cacheService;
        private readonly ILogger<DownloadController> _logger;

        public DownloadController(ITemporaryFileCacheService cacheService, ILogger<DownloadController> logger)
        {
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <summary>
        /// Descarga un archivo temporal usando su token de descarga
        /// </summary>
        /// <param name="token">Token de descarga del archivo</param>
        /// <returns>Archivo para descarga o error si no existe/expiró</returns>
        [HttpGet("{token}")]
        public async Task<IActionResult> DownloadFile(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest("Token de descarga requerido");
                }

                var fileInfo = await _cacheService.GetFileInfoAsync(token);
                if (fileInfo == null)
                {
                    _logger.LogWarning($"Intento de descarga con token inválido o expirado: {token}");
                    return NotFound("Archivo no encontrado o expirado");
                }

                var fileContent = await _cacheService.GetFileContentAsync(token);
                if (fileContent == null)
                {
                    _logger.LogError($"Error al leer contenido del archivo: {token}");
                    return StatusCode(500, "Error al leer el archivo");
                }

                _logger.LogInformation($"Descarga iniciada: {fileInfo.OriginalFileName}, Token: {token}");

                return File(
                    fileContent,
                    fileInfo.ContentType,
                    fileInfo.OriginalFileName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error durante la descarga del archivo: {token}");
                return StatusCode(500, "Error interno del servidor");
            }
        }

        /// <summary>
        /// Obtiene información de un archivo temporal sin descargarlo
        /// </summary>
        /// <param name="token">Token de descarga del archivo</param>
        /// <returns>Información del archivo</returns>
        [HttpGet("{token}/info")]
        public async Task<IActionResult> GetFileInfo(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest("Token de descarga requerido");
                }

                var fileInfo = await _cacheService.GetFileInfoAsync(token);
                if (fileInfo == null)
                {
                    return NotFound("Archivo no encontrado o expirado");
                }

                return Ok(new
                {
                    fileName = fileInfo.OriginalFileName,
                    contentType = fileInfo.ContentType,
                    fileSizeBytes = fileInfo.FileSizeBytes,
                    createdAt = fileInfo.CreatedAt,
                    expiresAt = fileInfo.ExpiresAt,
                    downloadToken = fileInfo.DownloadToken
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener información del archivo: {token}");
                return StatusCode(500, "Error interno del servidor");
            }
        }
    }
}