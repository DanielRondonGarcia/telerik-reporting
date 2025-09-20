using GenReports.Models; // Asegúrate de que ApiResponse y otros modelos estén accesibles
using GenReports.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Threading.Tasks;

namespace GenReports.Controllers
{
    /// <summary>
    /// Proporciona endpoints para la descarga e inspección de archivos temporales.
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
        /// Descarga un archivo temporal usando su token de acceso.
        /// </summary>
        /// <param name="token">El token único que identifica al archivo a descargar.</param>
        /// <returns>El contenido del archivo como un stream, o una respuesta de error si el token es inválido o ha expirado.</returns>
        [HttpGet("{token}")]
        [SwaggerOperation(
            Summary = "Descargar un archivo temporal",
            Description = "Utiliza un token de descarga para obtener el contenido binario de un archivo almacenado temporalmente.",
            OperationId = "DownloadFileByToken"
        )]
        [ProducesResponseType(typeof(FileContentResult), 200)] // Respuesta de éxito es un archivo
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(typeof(ApiResponse<object>), 404)]
        [ProducesResponseType(typeof(ApiResponse<object>), 500)]
        public async Task<IActionResult> DownloadFile(string token)
        {
            try
            {
                // 1. Lógica común de validación y obtención de metadatos extraída.
                var (fileInfo, errorResult) = await ValidateAndGetFileInfoAsync(token);
                if (errorResult != null)
                {
                    return errorResult;
                }

                // 2. Obtener el contenido del archivo.
                var fileContent = await _cacheService.GetFileContentAsync(token);
                if (fileContent == null)
                {
                    _logger.LogError("No se pudo leer el contenido del archivo para el token {Token}, aunque sus metadatos existen.", token);
                    return StatusCode(500, new ApiResponse<object> { Message = "Error interno al leer el contenido del archivo.", ErrorCode = "FILE_READ_ERROR" });
                }

                _logger.LogInformation("Descarga iniciada para el archivo {FileName} con token {Token}.", fileInfo!.OriginalFileName, token);

                // 3. Devolver el archivo. Esta es la forma correcta y no debe ser envuelta en ApiResponse.
                return File(fileContent, fileInfo.ContentType, fileInfo.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción no controlada durante la descarga del archivo con token {Token}.", token);
                return StatusCode(500, new ApiResponse<object> { Message = "Error interno del servidor.", ErrorCode = "INTERNAL_SERVER_ERROR" });
            }
        }

        /// <summary>
        /// Obtiene los metadatos de un archivo temporal sin descargarlo.
        /// </summary>
        /// <param name="token">El token único que identifica al archivo.</param>
        /// <returns>Un objeto con la información del archivo.</returns>
        [HttpGet("{token}/info")]
        [SwaggerOperation(
            Summary = "Obtener metadatos de un archivo temporal",
            Description = "Devuelve información sobre un archivo temporal, como su nombre, tamaño y fecha de expiración, sin descargar su contenido.",
            OperationId = "GetFileInfoByToken"
        )]
        [ProducesResponseType(typeof(ApiResponse<FileInfoResponse>), 200)] // Usando un DTO
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(typeof(ApiResponse<object>), 404)]
        [ProducesResponseType(typeof(ApiResponse<object>), 500)]
        public async Task<IActionResult> GetFileInfo(string token)
        {
            try
            {
                var (fileInfo, errorResult) = await ValidateAndGetFileInfoAsync(token);
                if (errorResult != null)
                {
                    return errorResult;
                }

                // 2. Mapear al DTO de respuesta para un contrato de API limpio.
                var response = new ApiResponse<FileInfoResponse>
                {
                    Success = true,
                    Data = new FileInfoResponse
                    {
                        FileName = fileInfo!.OriginalFileName,
                        ContentType = fileInfo.ContentType,
                        FileSizeBytes = fileInfo.FileSizeBytes,
                        CreatedAt = fileInfo.CreatedAt,
                        ExpiresAt = fileInfo.ExpiresAt,
                        DownloadToken = fileInfo.DownloadToken
                    }
                };
                
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción no controlada al obtener información del archivo con token {Token}.", token);
                return StatusCode(500, new ApiResponse<object> { Message = "Error interno del servidor.", ErrorCode = "INTERNAL_SERVER_ERROR" });
            }
        }

        /// <summary>
        /// Valida un token y recupera los metadatos del archivo asociado.
        /// </summary>
        /// <returns>Una tupla con la información del archivo y un IActionResult de error si la validación falla.</returns>
        private async Task<(TemporaryFileInfo? fileInfo, IActionResult? errorResult)> ValidateAndGetFileInfoAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return (null, BadRequest(new ApiResponse<object> { Message = "El token de descarga es requerido.", ErrorCode = "TOKEN_REQUIRED" }));
            }

            var fileInfo = await _cacheService.GetFileInfoAsync(token);
            if (fileInfo == null)
            {
                _logger.LogWarning("Intento de acceso con token inválido o expirado: {Token}", token);
                return (null, NotFound(new ApiResponse<object> { Message = "Archivo no encontrado o el enlace ha expirado.", ErrorCode = "FILE_NOT_FOUND" }));
            }

            return (fileInfo, null);
        }
    }
}