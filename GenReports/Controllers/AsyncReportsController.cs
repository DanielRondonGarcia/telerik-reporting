using Microsoft.AspNetCore.Mvc;
using GenReports.Services;
using GenReports.Models;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace GenReports.Controllers
{
    /// <summary>
    /// Controlador para manejar reportes asíncronos y grandes volúmenes de datos
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AsyncReportsController : ControllerBase
    {
        private readonly ReportQueueService _queueService;
        private readonly ITemporaryFileCacheService _cacheService;
        private readonly ILogger<AsyncReportsController> _logger;

        public AsyncReportsController(
            ReportQueueService queueService,
            ITemporaryFileCacheService cacheService,
            ILogger<AsyncReportsController> logger)
        {
            _queueService = queueService;
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <summary>
        /// Encola un reporte para procesamiento asíncrono usando multipart/form-data
        /// Recomendado para datasets grandes (>30,000 registros)
        /// </summary>
        /// <param name="reportType">Tipo de reporte a generar</param>
        /// <param name="userName">Usuario que solicita el reporte</param>
        /// <param name="dataFile">Archivo JSON con los datos del reporte</param>
        /// <param name="isLargeDataset">Indica si es un dataset grande que requiere procesamiento especial</param>
        /// <returns>ID del trabajo encolado</returns>
        [HttpPost("queue-multipart")]
        [RequestSizeLimit(500_000_000)] // 500MB límite
        [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> QueueReportMultipart(
            [FromForm, Required] string reportType,
            [FromForm, Required] string userName,
            [Required] IFormFile dataFile,
            [FromForm] bool isLargeDataset = true)
        {
            try
            {
                // Validaciones
                if (string.IsNullOrWhiteSpace(reportType))
                    return BadRequest("El tipo de reporte es requerido");

                if (string.IsNullOrWhiteSpace(userName))
                    return BadRequest("El nombre de usuario es requerido");

                if (dataFile == null || dataFile.Length == 0)
                    return BadRequest("El archivo de datos es requerido");

                if (dataFile.Length > 500_000_000) // 500MB
                    return BadRequest("El archivo excede el tamaño máximo permitido (500MB)");

                // Verificar que sea un archivo JSON
                if (!dataFile.ContentType.Contains("json") && !dataFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("El archivo debe ser de tipo JSON");

                // Leer contenido del archivo
                string jsonData;
                using (var reader = new StreamReader(dataFile.OpenReadStream()))
                {
                    jsonData = await reader.ReadToEndAsync();
                }

                // Validar que sea JSON válido
                try
                {
                    JsonDocument.Parse(jsonData);
                }
                catch (JsonException)
                {
                    return BadRequest("El contenido del archivo no es JSON válido");
                }

                // Generar ID único para el trabajo
                var jobId = Guid.NewGuid().ToString();

                // Encolar el trabajo
                var enqueuedJobId = _queueService.EnqueueReportJob(jobId, jsonData, reportType, userName, isLargeDataset);

                _logger.LogInformation($"Reporte encolado vía multipart: {enqueuedJobId} para usuario {userName}, tamaño: {dataFile.Length} bytes");

                return Ok(new
                {
                    JobId = enqueuedJobId,
                    Message = "Reporte encolado para procesamiento asíncrono",
                    EstimatedProcessingTime = isLargeDataset ? "5-15 minutos" : "2-5 minutos",
                    StatusCheckUrl = $"/api/AsyncReports/status/{enqueuedJobId}",
                    DataSizeBytes = dataFile.Length,
                    IsLargeDataset = isLargeDataset
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encolando reporte multipart");
                return StatusCode(500, new { Error = "Error interno del servidor", Details = ex.Message });
            }
        }

        /// <summary>
        /// Encola un reporte para procesamiento asíncrono usando JSON en el body (para datasets medianos)
        /// </summary>
        /// <param name="request">Datos del reporte</param>
        /// <returns>ID del trabajo encolado</returns>
        [HttpPost("queue-json")]
        [RequestSizeLimit(100_000_000)] // 100MB límite para JSON
        public async Task<IActionResult> QueueReportJson([FromBody] AsyncReportRequest request)
        {
            try
            {
                // Validaciones
                if (string.IsNullOrWhiteSpace(request.ReportType))
                    return BadRequest("El tipo de reporte es requerido");

                if (string.IsNullOrWhiteSpace(request.UserName))
                    return BadRequest("El nombre de usuario es requerido");

                if (string.IsNullOrWhiteSpace(request.JsonData))
                    return BadRequest("Los datos JSON son requeridos");

                // Validar que sea JSON válido
                try
                {
                    JsonDocument.Parse(request.JsonData);
                }
                catch (JsonException)
                {
                    return BadRequest("Los datos no son JSON válido");
                }

                // Generar ID único para el trabajo
                var jobId = Guid.NewGuid().ToString();

                // Determinar si es dataset grande basado en el tamaño
                var jsonSizeBytes = System.Text.Encoding.UTF8.GetByteCount(request.JsonData);
                var isLargeDataset = request.IsLargeDataset ?? jsonSizeBytes > 10_000_000; // 10MB

                // Encolar el trabajo
                var enqueuedJobId = _queueService.EnqueueReportJob(jobId, request.JsonData, request.ReportType, request.UserName, isLargeDataset);

                _logger.LogInformation($"Reporte encolado vía JSON: {enqueuedJobId} para usuario {request.UserName}, tamaño: {jsonSizeBytes} bytes");

                return Ok(new
                {
                    JobId = enqueuedJobId,
                    Message = "Reporte encolado para procesamiento asíncrono",
                    EstimatedProcessingTime = isLargeDataset ? "5-15 minutos" : "2-5 minutos",
                    StatusCheckUrl = $"/api/AsyncReports/status/{enqueuedJobId}",
                    DataSizeBytes = jsonSizeBytes,
                    IsLargeDataset = isLargeDataset
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encolando reporte JSON");
                return StatusCode(500, new { Error = "Error interno del servidor", Details = ex.Message });
            }
        }

        /// <summary>
        /// Obtiene el estado de un trabajo de reporte
        /// </summary>
        /// <param name="jobId">ID del trabajo</param>
        /// <returns>Estado del trabajo</returns>
        [HttpGet("status/{jobId}")]
        public IActionResult GetJobStatus(string jobId)
        {
            try
            {
                var status = _queueService.GetJobStatus(jobId);
                
                if (status == null)
                    return NotFound(new { Error = "Trabajo no encontrado", JobId = jobId });

                var response = new
                {
                    JobId = status.JobId,
                    Status = status.Status.ToString(),
                    Message = status.Message,
                    CreatedAt = status.CreatedAt,
                    CompletedAt = status.CompletedAt,
                    FileName = status.FileName,
                    FileSizeBytes = status.FileSizeBytes,
                    DownloadUrl = status.Status == ReportJobStatusEnum.Completed && !string.IsNullOrEmpty(status.DownloadToken)
                        ? $"/api/AsyncReports/download/{status.DownloadToken}"
                        : null,
                    PerformanceMetrics = status.PerformanceMetrics != null ? new
                    {
                        ProcessingTimeMs = status.PerformanceMetrics.TotalExecutionTimeMs,
                        RecordsProcessed = status.PerformanceMetrics.RecordsProcessed
                    } : null
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error obteniendo estado del trabajo: {jobId}");
                return StatusCode(500, new { Error = "Error interno del servidor" });
            }
        }

        /// <summary>
        /// Descarga un archivo de reporte completado
        /// </summary>
        /// <param name="downloadToken">Token de descarga</param>
        /// <returns>Archivo del reporte</returns>
        [HttpGet("download/{downloadToken}")]
        public async Task<IActionResult> DownloadReport(string downloadToken)
        {
            try
            {
                var fileInfo = await _cacheService.GetFileInfoAsync(downloadToken);
                
                if (fileInfo == null)
                    return NotFound(new { Error = "Archivo no encontrado o expirado" });

                var fileBytes = await _cacheService.GetFileContentAsync(downloadToken);
                
                if (fileBytes == null)
                    return NotFound(new { Error = "No se pudo acceder al archivo" });

                _logger.LogInformation($"Descargando archivo: {fileInfo.OriginalFileName} ({fileInfo.FileSizeBytes} bytes)");

                return File(fileBytes, fileInfo.ContentType, fileInfo.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error descargando archivo: {downloadToken}");
                return StatusCode(500, new { Error = "Error interno del servidor" });
            }
        }

        /// <summary>
        /// Obtiene la lista de trabajos activos
        /// </summary>
        /// <returns>Lista de trabajos activos</returns>
        [HttpGet("active-jobs")]
        public IActionResult GetActiveJobs()
        {
            try
            {
                var activeJobs = _queueService.GetActiveJobs()
                    .Select(job => new
                    {
                        JobId = job.JobId,
                        Status = job.Status.ToString(),
                        Message = job.Message,
                        CreatedAt = job.CreatedAt,
                        FileName = job.FileName,
                        FileSizeBytes = job.FileSizeBytes
                    })
                    .ToList();

                return Ok(new
                {
                    TotalActiveJobs = activeJobs.Count,
                    Jobs = activeJobs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo trabajos activos");
                return StatusCode(500, new { Error = "Error interno del servidor" });
            }
        }

        /// <summary>
        /// Obtiene estadísticas del caché de archivos
        /// </summary>
        /// <returns>Estadísticas del caché</returns>
        [HttpGet("cache-stats")]
        public async Task<IActionResult> GetCacheStatistics()
        {
            try
            {
                // TODO: Implementar método GetCacheStatistics en ITemporaryFileCacheService
                var stats = new 
                {
                    Message = "Estadísticas del caché no implementadas aún",
                    Timestamp = DateTime.UtcNow
                };
                
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo estadísticas del caché");
                return StatusCode(500, new { Error = "Error interno del servidor" });
            }
        }
    }

    /// <summary>
    /// Modelo para solicitud de reporte asíncrono vía JSON
    /// </summary>
    public class AsyncReportRequest
    {
        [Required]
        public string ReportType { get; set; } = string.Empty;
        
        [Required]
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public string JsonData { get; set; } = string.Empty;
        
        public bool? IsLargeDataset { get; set; }
    }
}