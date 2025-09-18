using GenReports.business;
using GenReports.Models;
using GenReports.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Cryptography;
using System.Text.Json;

namespace GenReports.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
        private readonly ITemporaryFileCacheService _cacheService;
        private readonly Report _reportService;

        public ReportesController(ITemporaryFileCacheService cacheService, Report reportService)
        {
            _cacheService = cacheService;
            _reportService = reportService;
        }

        /// <summary>
        /// Genera reportes basados en los datos JSON proporcionados.
        /// - Para un solo registro: retorna un archivo PDF individual
        /// - Para múltiples registros: genera reportes individuales por cada registro y los comprime en un archivo ZIP
        /// </summary>
        /// <param name="dataSource">Datos en formato JSON para generar el reporte. Debe contener una propiedad "Data" con los registros</param>
        /// <param name="reportType">Tipo de reporte a generar (opcional, por defecto "USUARIO")</param>
        /// <param name="userName">Nombre del usuario que genera el reporte (opcional, por defecto "SYSTEM")</param>
        /// <returns>
        /// - Archivo PDF para un solo registro
        /// - Archivo ZIP con múltiples PDFs para múltiples registros
        /// </returns>
        /// <remarks>
        /// El endpoint detecta automáticamente si se envían múltiples registros en el array "Data"
        /// y genera reportes individuales comprimidos para optimizar la transferencia de datos masivos.
        ///
        /// Ejemplo de JSON para un solo registro:
        /// {
        ///   "Data": { "campo1": "valor1", "campo2": "valor2" }
        /// }
        ///
        /// Ejemplo de JSON para múltiples registros:
        /// {
        ///   "Data": [
        ///     { "campo1": "valor1", "campo2": "valor2" },
        ///     { "campo1": "valor3", "campo2": "valor4" }
        ///   ]
        /// }
        ///
        /// Ejemplo completo de request body:
        ///
        ///     POST /api/reportes/telerik/json/file/batch
        ///     {
        ///       "Data": [
        ///         {
        ///           "AppUser": "AACOSTAA",
        ///           "IdentificactionCard": 1098613733,
        ///           "Name": "ANDRES F. ACOSTA AVELLANEDA",
        ///           "Zone": "11",
        ///           "ZoneDescription": "ZONA CENTR0",
        ///           "Dependency": "1",
        ///           "DependencyDescription": null,
        ///           "Office": "0",
        ///           "OfficeDescription": "DEPENDENCIA NO ASIGNADA..",
        ///           "Role": "0",
        ///           "RoleDescription": "SIN DEFINIR",
        ///           "Mail": null,
        ///           "Extension": 1,
        ///           "Supervisor": "AACOSTAA",
        ///           "SupervisorName": "ANDRES F. ACOSTA AVELLANEDA",
        ///           "Type": "B",
        ///           "TypeDescription": "BASE DE DATOS",
        ///           "MaximunSesssion": 5,
        ///           "Status": "N",
        ///           "Technician": "N",
        ///           "Printer": null,
        ///           "AuxiliaryPrinter": null,
        ///           "CellPhone": null,
        ///           "IssuanceCedula": null,
        ///           "Password": null,
        ///           "DeactivationDate": "2023-06-07T16:34:30",
        ///           "Photo": null,
        ///           "CompanyWork": null,
        ///           "CompanyWorkName": null,
        ///           "HasAuditProfile": false,
        ///           "DbStatus": "N",
        ///           "AccountStatus": null
        ///         }
        ///       ]
        ///     }
        ///
        /// </remarks>
        /// <response code="200">Reporte generado exitosamente (PDF individual o ZIP con múltiples PDFs)</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpPost("telerik/json/file/batch")]
        [SwaggerOperation(
            Summary = "Generar reportes con datos JSON (individual o masivo)",
            Description = "Genera reportes PDF usando Telerik con los datos JSON proporcionados. Detecta automáticamente si es un registro único o múltiples registros y genera la salida correspondiente (PDF individual o ZIP con múltiples PDFs).",
            OperationId = "GenerateReport"
        )]
        [SwaggerResponse(200, "Reporte(s) generado(s) exitosamente", typeof(ApiResponse<UFileDownload>))]
        [SwaggerResponse(500, "Error interno del servidor", typeof(ApiResponse<UFileDownload>))]
        public async Task<IActionResult> GenerateReport(
            [FromBody]
            [SwaggerRequestBody(
                Description = "Datos JSON para generar el reporte. Puede contener cualquier estructura JSON válida.",
                Required = true
            )]
            object dataSource,
            [FromQuery]
            [SwaggerParameter(
                Description = "Tipo de reporte a generar",
                Required = false
            )]
            string reportType = "USUARIO",
            [FromQuery]
            [SwaggerParameter(
                Description = "Nombre del usuario que genera el reporte",
                Required = false
            )]
            string userName = "SYSTEM")
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ArchivoResult? fileOutput = null;

            try
            {
                Console.WriteLine($"=== ENDPOINT BATCH INICIADO ===");
                Console.WriteLine($"ReportType: {reportType}, UserName: {userName}");

                // Convertir el objeto a JSON string
                var jsonString = JsonSerializer.Serialize(dataSource);
                Console.WriteLine($"JSON recibido: {jsonString}");

                // Calcular hash MD5 de los datos de entrada
                string inputHash;
                using (var md5 = MD5.Create())
                {
                    var inputBytes = System.Text.Encoding.UTF8.GetBytes($"{jsonString}_{reportType}_{userName}");
                    var hashBytes = md5.ComputeHash(inputBytes);
                    inputHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                Console.WriteLine($"Hash MD5 calculado: {inputHash}");

                // Verificar si ya existe en caché
                var cachedFile = await _cacheService.FindByMD5HashAsync(inputHash);
                if (cachedFile != null)
                {
                    Console.WriteLine($"Archivo encontrado en caché: {cachedFile.OriginalFileName}");

                    // Generar URL de descarga
                    var requestBaseUrl = $"{Request.Scheme}://{Request.Host}";
                    var downloadUrl = _cacheService.GenerateDownloadUrl(cachedFile.DownloadToken, requestBaseUrl);

                    var cachedResponse = new ApiResponse<UFileDownload>
                    {
                        Data = new UFileDownload
                        {
                            NombreArchivo = cachedFile.OriginalFileName,
                            ContentType = cachedFile.ContentType,
                            UrlDescarga = downloadUrl,
                            FechaExpiracion = cachedFile.ExpiresAt,
                            MD5Hash = inputHash,
                            EncontradoEnCache = true
                        },
                        Success = true,
                        Message = "Reporte obtenido desde caché temporal"
                    };

                    stopwatch.Stop();
                    Console.WriteLine($"=== ENDPOINT BATCH COMPLETADO (CACHÉ) en {stopwatch.ElapsedMilliseconds}ms ===");
                    return Ok(cachedResponse);
                }

                Console.WriteLine("Archivo no encontrado en caché, generando nuevo reporte...");

                // Usar instancia del negocio de reportes inyectada
                var reporteNegocio = _reportService;

                // Determinar si es un reporte masivo (múltiples registros)
                var isMultipleRecords = IsMultipleRecords(jsonString);

                if (isMultipleRecords)
                {
                    Console.WriteLine("Detectados múltiples registros - Generando reportes individuales comprimidos");
                    // Generar reportes individuales comprimidos
                    fileOutput = await reporteNegocio.ExecuteBatchReportsCompressed(jsonString, reportType, userName);
                }
                else
                {
                    Console.WriteLine("Detectado registro único - Generando reporte individual");
                    // Generar reporte único como antes
                    fileOutput = reporteNegocio.ExecuteReport(jsonString, reportType, userName);
                }

                if (fileOutput == null)
                {
                    throw new InvalidOperationException("No se pudo generar el reporte");
                }

                // Detectar tipo de contenido basado en la extensión
                var contentType = fileOutput.NombreArchivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? "application/zip"
                    : "application/pdf";

                // Almacenar en caché temporal con hash personalizado
                var tempFileInfo = await _cacheService.StoreFileAsync(
                    fileOutput.BytesArchivo,
                    fileOutput.NombreArchivo,
                    contentType,
                    inputHash
                );

                // Generar URL de descarga
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var downloadUrl2 = _cacheService.GenerateDownloadUrl(tempFileInfo.DownloadToken, baseUrl);

                stopwatch.Stop();
                Console.WriteLine($"=== ENDPOINT BATCH COMPLETADO en {stopwatch.ElapsedMilliseconds}ms ===");

                var response = new ApiResponse<UFileDownload>
                {
                    Data = new UFileDownload
                    {
                        NombreArchivo = tempFileInfo.OriginalFileName,
                        ContentType = contentType,
                        UrlDescarga = downloadUrl2,
                        FechaExpiracion = tempFileInfo.ExpiresAt,
                        MD5Hash = inputHash,
                        EncontradoEnCache = false
                    },
                    Success = true,
                    Message = $"Reporte generado exitosamente en {stopwatch.ElapsedMilliseconds}ms"
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"Error en endpoint batch después de {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                var errorResponse = new ApiResponse<UFileDownload>
                {
                    Data = null,
                    Success = false,
                    Message = $"Error al generar el reporte: {ex.Message}",
                    ErrorCode = "REPORT_GENERATION_ERROR"
                };

                return StatusCode(500, errorResponse);
            }
        }

        /// <summary>
        /// Determina si el JSON contiene múltiples registros
        /// </summary>
        /// <param name="jsonString">JSON string a analizar</param>
        /// <returns>True si contiene múltiples registros, False si es un solo registro</returns>
        private bool IsMultipleRecords(string jsonString)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonString);
                var root = document.RootElement;

                // Intentar obtener la propiedad "Data" sin sensibilidad a mayúsculas/minúsculas
                JsonElement dataElement;
                if (!TryGetPropertyCaseInsensitive(root, "Data", out dataElement))
                {
                    // Si no existe la propiedad Data, aceptar también cuando la raíz es un arreglo
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        var rootArrayLength = root.GetArrayLength();
                        Console.WriteLine($"Número de registros detectados (raíz como arreglo): {rootArrayLength}");
                        return rootArrayLength > 1;
                    }

                    // Si no es arreglo, considerarlo como un solo registro
                    return false;
                }

                // Si Data es un array con más de un elemento, es masivo
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    var arrayLength = dataElement.GetArrayLength();
                    Console.WriteLine($"Número de registros detectados: {arrayLength}");
                    return arrayLength > 1;
                }

                // Si Data no es un array, es un solo registro
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analizando JSON para detectar múltiples registros: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Calcula el hash MD5 de una cadena de texto
        /// </summary>
        /// <param name="input">Texto de entrada</param>
        /// <returns>Hash MD5 en formato hexadecimal</returns>
        private string CalculateMD5Hash(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Genera reportes usando el método consolidado + split para comparación de rendimiento.
        /// Genera un PDF consolidado con todos los registros y luego lo divide en archivos individuales.
        /// </summary>
        /// <param name="dataSource">Datos en formato JSON para generar el reporte. Debe contener una propiedad "Data" con los registros</param>
        /// <param name="reportType">Tipo de reporte a generar</param>
        /// <param name="userName">Nombre del usuario que solicita el reporte</param>
        /// <returns>Archivo ZIP con múltiples PDFs generados mediante consolidación + split</returns>
        /// <remarks>
        /// Este endpoint implementa una estrategia diferente al endpoint /batch:
        /// 1. Genera UN solo reporte PDF consolidado con todos los registros
        /// 2. Aplica split usando PdfStreamWriter de Telerik para dividirlo en archivos individuales
        /// 3. Comprime los archivos resultantes en un ZIP
        ///
        /// Úsalo para comparar rendimiento contra el método de generación individual (/batch).
        ///
        /// Ejemplo de JSON:
        /// {
        ///   "Data": [
        ///     { "campo1": "valor1", "campo2": "valor2" },
        ///     { "campo1": "valor3", "campo2": "valor4" }
        ///   ]
        /// }
        /// </remarks>
        [HttpPost("telerik/json/file/consolidated-split")]
        [ProducesResponseType(typeof(UFileDownload), 200)]
        [ProducesResponseType(typeof(ApiResponse<string>), 400)]
        [ProducesResponseType(typeof(ApiResponse<string>), 500)]
        public async Task<IActionResult> GenerateConsolidatedReportWithSplit(
            [FromBody]
            [SwaggerRequestBody(
                Description = "Datos JSON para generar el reporte consolidado",
                Required = true
            )]
            object dataSource,
            [FromQuery]
            [SwaggerParameter(
                Description = "Tipo de reporte a generar",
                Required = false
            )]
            string reportType = "USUARIO_MASIVO",
            [FromQuery]
            [SwaggerParameter(
                Description = "Nombre del usuario que solicita el reporte",
                Required = false
            )]
            string userName = "SYSTEM")
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ArchivoResult fileOutput = null;

            try
            {
                Console.WriteLine($"=== ENDPOINT CONSOLIDADO+SPLIT INICIADO ===");
                Console.WriteLine($"ReportType: {reportType}, UserName: {userName}");

                // Convertir el objeto a JSON string
                var jsonString = JsonSerializer.Serialize(dataSource);
                Console.WriteLine($"JSON recibido: {jsonString}");

                // Validar que el JSON no esté vacío
                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    throw new ArgumentException("El JSON del reporte no puede estar vacío", nameof(jsonString));
                }

                // Verificar que hay múltiples registros
                var isMultipleRecords = IsMultipleRecords(jsonString);
                if (!isMultipleRecords)
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Este endpoint requiere múltiples registros. Para un solo registro use el endpoint /batch",
                        ErrorCode = "SINGLE_RECORD_NOT_SUPPORTED"
                    });
                }

                // Calcular hash MD5 de los datos de entrada
                string inputHash;
                using (var md5 = MD5.Create())
                {
                    var inputBytes = System.Text.Encoding.UTF8.GetBytes($"{jsonString}_consolidated");
                    var hashBytes = md5.ComputeHash(inputBytes);
                    inputHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                Console.WriteLine($"Hash MD5 calculado: {inputHash}");

                // Verificar si ya existe en caché
                var cachedFile = await _cacheService.FindByMD5HashAsync(inputHash);
                if (cachedFile != null)
                {
                    Console.WriteLine($"Archivo encontrado en caché: {cachedFile.OriginalFileName}");

                    // Generar URL de descarga
                    var requestBaseUrl = $"{Request.Scheme}://{Request.Host}";
                    var downloadUrl = _cacheService.GenerateDownloadUrl(cachedFile.DownloadToken, requestBaseUrl);

                    var cachedResponse = new ApiResponse<UFileDownload>
                    {
                        Data = new UFileDownload
                        {
                            NombreArchivo = cachedFile.OriginalFileName,
                            ContentType = cachedFile.ContentType,
                            UrlDescarga = downloadUrl,
                            FechaExpiracion = cachedFile.ExpiresAt,
                            MD5Hash = inputHash,
                            EncontradoEnCache = true
                        },
                        Success = true,
                        Message = "Reporte consolidado obtenido desde caché temporal"
                    };

                    stopwatch.Stop();
                    Console.WriteLine($"=== ENDPOINT CONSOLIDADO+SPLIT COMPLETADO (CACHÉ) en {stopwatch.ElapsedMilliseconds}ms ===");
                    return Ok(cachedResponse);
                }

                Console.WriteLine("Archivo no encontrado en caché, generando nuevo reporte consolidado...");

                // Usar instancia del negocio de reportes inyectada
                var reporteNegocio = _reportService;

                Console.WriteLine("Generando reporte consolidado y aplicando split...");
                // Generar reporte consolidado y aplicar split
                fileOutput = await reporteNegocio.ExecuteConsolidatedReportWithSplit(jsonString, reportType, userName);

                if (fileOutput == null)
                {
                    throw new InvalidOperationException("No se pudo generar el reporte consolidado");
                }

                // Detectar tipo de contenido basado en la extensión
                var contentType = fileOutput.NombreArchivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? "application/zip"
                    : "application/pdf";

                // Almacenar en caché temporal con hash personalizado
                var tempFileInfo = await _cacheService.StoreFileAsync(
                    fileOutput.BytesArchivo,
                    fileOutput.NombreArchivo,
                    contentType,
                    inputHash
                );

                // Generar URL de descarga
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var downloadUrl2 = _cacheService.GenerateDownloadUrl(tempFileInfo.DownloadToken, baseUrl);

                stopwatch.Stop();
                Console.WriteLine($"=== ENDPOINT CONSOLIDADO+SPLIT COMPLETADO en {stopwatch.ElapsedMilliseconds}ms ===");

                var response = new ApiResponse<UFileDownload>
                {
                    Data = new UFileDownload
                    {
                        NombreArchivo = tempFileInfo.OriginalFileName,
                        ContentType = contentType,
                        UrlDescarga = downloadUrl2,
                        FechaExpiracion = tempFileInfo.ExpiresAt,
                        MD5Hash = inputHash,
                        EncontradoEnCache = false
                    },
                    Success = true,
                    Message = $"Reporte consolidado con split generado exitosamente en {stopwatch.ElapsedMilliseconds}ms"
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"Error en endpoint consolidado+split después de {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                var errorResponse = new ApiResponse<UFileDownload>
                {
                    Data = null,
                    Success = false,
                    Message = $"Error generando reporte consolidado con split: {ex.Message}",
                    ErrorCode = "CONSOLIDATED_SPLIT_ERROR"
                };

                return StatusCode(500, errorResponse);
            }
        }

        // Helper local para leer propiedades de forma case-insensitive
        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }
    }
}