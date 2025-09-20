// ReportesController.cs
using GenReports.business;
using GenReports.Models;
using GenReports.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // 1. Usar ILogger
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenReports.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
        private readonly ITemporaryFileCacheService _cacheService;
        private readonly Report _reportService;
        private readonly ILogger<ReportesController> _logger; // 2. Inyectar ILogger

        public ReportesController(
            ITemporaryFileCacheService cacheService,
            Report reportService,
            ILogger<ReportesController> logger) // Inyectar en el constructor
        {
            _cacheService = cacheService;
            _reportService = reportService;
            _logger = logger;
        }

        [HttpPost("telerik/json/file/batch")]
        [SwaggerOperation(
            Summary = "Generar reportes con datos JSON (individual o masivo)",
            Description = "Genera reportes PDF usando Telerik. Detecta si es un registro único o múltiple y genera la salida correspondiente (PDF individual o ZIP/7z).",
            OperationId = "GenerateReport"
        )]
        [SwaggerResponse(200, "Reporte(s) generado(s) exitosamente", typeof(ApiResponse<UFileDownload>))]
        [SwaggerResponse(500, "Error interno del servidor", typeof(ApiResponse<UFileDownload>))]
        public async Task<IActionResult> GenerateReport(
            [FromBody] object dataSource,
            [FromQuery] string reportType = "USUARIO",
            [FromQuery] string userName = "SYSTEM")
        {
            var jsonString = JsonSerializer.Serialize(dataSource);
            
            // La lógica específica de este endpoint es decidir qué método de servicio llamar.
            Func<Task<ArchivoResult>> reportGenerator = IsMultipleRecords(jsonString)
                ? () =>
                {
                    _logger.LogInformation("Detectados múltiples registros. Generando reportes individuales comprimidos.");
                    return _reportService.ExecuteBatchReportsCompressed(jsonString, reportType, userName);
                }
                : () =>
                {
                    _logger.LogInformation("Detectado registro único. Generando reporte individual.");
                    return Task.FromResult(_reportService.ExecuteReport(jsonString, reportType, userName));
                };
            
            // Usar el método centralizado para procesar la solicitud
            return await ProcessReportRequestAsync(
                jsonString,
                reportType,
                userName,
                "batch",
                "REPORT_GENERATION_ERROR",
                reportGenerator);
        }

        [HttpPost("telerik/json/file/consolidated-split")]
        [SwaggerOperation(
            Summary = "Generar reportes masivos con estrategia de consolidación y división",
            Description = "Genera un único PDF consolidado y luego lo divide en archivos individuales, ideal para comparar rendimiento.",
            OperationId = "GenerateConsolidatedReportWithSplit"
        )]
        [ProducesResponseType(typeof(ApiResponse<UFileDownload>), 200)]
        [ProducesResponseType(typeof(ApiResponse<string>), 400)]
        [ProducesResponseType(typeof(ApiResponse<string>), 500)]
        public async Task<IActionResult> GenerateConsolidatedReportWithSplit(
            [FromBody] object dataSource,
            [FromQuery] string reportType = "USUARIO_MASIVO",
            [FromQuery] string userName = "SYSTEM")
        {
            var jsonString = JsonSerializer.Serialize(dataSource);

            if (!IsMultipleRecords(jsonString))
            {
                return BadRequest(new ApiResponse<string>
                {
                    Success = false,
                    Message = "Este endpoint requiere múltiples registros. Para un solo registro use el endpoint /batch",
                    ErrorCode = "SINGLE_RECORD_NOT_SUPPORTED"
                });
            }

            // La lógica específica es llamar al método de consolidación.
            Func<Task<ArchivoResult>> reportGenerator = () =>
            {
                _logger.LogInformation("Generando reporte consolidado y aplicando split.");
                return _reportService.ExecuteConsolidatedReportWithSplit(jsonString, reportType, userName);
            };
            
            // Usar el método centralizado
            return await ProcessReportRequestAsync(
                jsonString,
                reportType,
                userName,
                "consolidated-split",
                "CONSOLIDATED_SPLIT_ERROR",
                reportGenerator);
        }

        /// <summary>
        /// Método centralizado para procesar una solicitud de generación de reporte, incluyendo caché,
        /// ejecución, almacenamiento y manejo de errores.
        /// </summary>
        /// <param name="jsonString">El JSON de entrada para el reporte.</param>
        /// <param name="reportType">El tipo de reporte.</param>
        /// <param name="userName">El nombre de usuario.</param>
        /// <param name="cacheKeySuffix">Un sufijo para diferenciar los hashes de caché entre endpoints.</param>
        /// <param name="errorCode">El código de error a devolver en caso de fallo.</param>
        /// <param name="reportGenerator">Función que ejecuta la generación del reporte real.</param>
        /// <returns>Un IActionResult con la respuesta de la API.</returns>
        private async Task<IActionResult> ProcessReportRequestAsync(
            string jsonString,
            string reportType,
            string userName,
            string cacheKeySuffix,
            string errorCode,
            Func<Task<ArchivoResult>> reportGenerator)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Procesando solicitud de reporte para {ReportType} por {UserName} con sufijo {CacheSuffix}.",
                reportType, userName, cacheKeySuffix);

            try
            {
                var inputHash = CalculateMD5Hash($"{jsonString}_{reportType}_{userName}_{cacheKeySuffix}");
                _logger.LogInformation("Hash MD5 de entrada calculado: {InputHash}", inputHash);

                // 1. Verificar si ya existe en caché
                var cachedFile = await _cacheService.FindByMD5HashAsync(inputHash);
                if (cachedFile != null)
                {
                    stopwatch.Stop();
                    _logger.LogInformation("Archivo encontrado en caché: {FileName}. Proceso completado en {ElapsedMilliseconds}ms.",
                        cachedFile.OriginalFileName, stopwatch.ElapsedMilliseconds);
                    
                    return Ok(CreateCachedApiResponse(cachedFile, inputHash));
                }

                _logger.LogInformation("Archivo no encontrado en caché. Generando nuevo reporte...");

                // 2. Generar el reporte usando la función proporcionada
                var fileOutput = await reportGenerator();
                if (fileOutput?.BytesArchivo == null || fileOutput.BytesArchivo.Length == 0)
                {
                    throw new InvalidOperationException("El servicio de reportes no devolvió ningún contenido.");
                }

                // 3. Almacenar en caché y crear la respuesta
                var contentType = UFile.GetContentTypeFromFileName(fileOutput.NombreArchivo);
                var tempFileInfo = await _cacheService.StoreFileAsync(
                    fileOutput.BytesArchivo,
                    fileOutput.NombreArchivo,
                    contentType,
                    inputHash
                );

                stopwatch.Stop();
                _logger.LogInformation("Reporte generado y almacenado exitosamente en {ElapsedMilliseconds}ms.", stopwatch.ElapsedMilliseconds);

                return Ok(CreateSuccessApiResponse(tempFileInfo, inputHash, stopwatch.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error generando reporte para {ReportType} después de {ElapsedMilliseconds}ms. Error: {ErrorMessage}",
                    reportType, stopwatch.ElapsedMilliseconds, ex.Message);
                
                return StatusCode(500, new ApiResponse<UFileDownload>
                {
                    Success = false,
                    Message = $"Error al generar el reporte: {ex.Message}",
                    ErrorCode = errorCode
                });
            }
        }

        #region Helper Methods

        private bool IsMultipleRecords(string jsonString)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonString);
                var root = document.RootElement;

                if (TryGetPropertyCaseInsensitive(root, "Data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    return dataElement.GetArrayLength() > 1;
                }
                
                if (root.ValueKind == JsonValueKind.Array)
                {
                    return root.GetArrayLength() > 1;
                }

                return false;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error al analizar JSON para detectar múltiples registros. Se asumirá un solo registro.");
                return false;
            }
        }

        private string CalculateMD5Hash(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            return false;
        }

        private ApiResponse<UFileDownload> CreateSuccessApiResponse(TemporaryFileInfo tempFileInfo, string md5Hash, long elapsedMilliseconds)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var downloadUrl = _cacheService.GenerateDownloadUrl(tempFileInfo.DownloadToken, baseUrl);

            return new ApiResponse<UFileDownload>
            {
                Data = new UFileDownload
                {
                    NombreArchivo = tempFileInfo.OriginalFileName,
                    ContentType = tempFileInfo.ContentType,
                    UrlDescarga = downloadUrl,
                    FechaExpiracion = tempFileInfo.ExpiresAt,
                    MD5Hash = md5Hash,
                    EncontradoEnCache = false
                },
                Success = true,
                Message = $"Reporte generado exitosamente en {elapsedMilliseconds}ms"
            };
        }

        private ApiResponse<UFileDownload> CreateCachedApiResponse(TemporaryFileInfo cachedFile, string md5Hash)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var downloadUrl = _cacheService.GenerateDownloadUrl(cachedFile.DownloadToken, baseUrl);

            return new ApiResponse<UFileDownload>
            {
                Data = new UFileDownload
                {
                    NombreArchivo = cachedFile.OriginalFileName,
                    ContentType = cachedFile.ContentType,
                    UrlDescarga = downloadUrl,
                    FechaExpiracion = cachedFile.ExpiresAt,
                    MD5Hash = md5Hash,
                    EncontradoEnCache = true
                },
                Success = true,
                Message = "Reporte obtenido desde caché temporal"
            };
        }

        #endregion
    }
}
