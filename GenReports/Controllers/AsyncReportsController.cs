using GenReports.Models;
using GenReports.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenReports.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [SwaggerTag("Maneja la generación asíncrona de reportes para grandes volúmenes de datos")]
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

        [HttpPost("queue-multipart")]
        [RequestSizeLimit(1_073_741_824)] // 1 GB
        [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
        [Consumes("multipart/form-data")]
        [SwaggerOperation(
            Summary = "Encola un reporte para procesamiento asíncrono",
            Description = "Sube un archivo JSON grande para ser procesado en segundo plano. Devuelve un ID de trabajo para verificar el estado. Si un reporte idéntico ya fue procesado y está en caché, devuelve la URL de descarga directamente.",
            OperationId = "QueueReport"
        )]
        [ProducesResponseType(typeof(ApiResponse<QueueReportResponse>), 202)]
        [ProducesResponseType(typeof(ApiResponse<CachedReportResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(typeof(ApiResponse<object>), 500)]
        public async Task<IActionResult> QueueReportMultipart(
            [FromForm, Required] string reportType,
            [FromForm, Required] string userName,
            [FromForm, Required] IFormFile dataFile,
            [FromForm] string processingMode = "batch")
        {
            // --- Validación de Entrada ---
            var validationError = ValidateQueueRequest(dataFile, processingMode);
            if (validationError != null) return validationError;

            var mode = processingMode.Trim().ToLowerInvariant();

            try
            {
                using var reader = new StreamReader(dataFile.OpenReadStream());
                var jsonData = await reader.ReadToEndAsync();

                // --- Lógica de Caché ---
                var requestHash = CreateRequestCacheHash(reportType, mode, jsonData);
                var cachedFile = await _cacheService.FindByMD5HashAsync(requestHash);
                if (cachedFile != null)
                {
                    _logger.LogInformation("Reporte encontrado en caché para el hash {RequestHash}. Devolviendo resultado directamente.", requestHash);
                    var cachedResponse = new ApiResponse<CachedReportResponse>
                    {
                        Success = true,
                        Data = new CachedReportResponse(cachedFile, GenerateDownloadUrl(cachedFile.DownloadToken))
                    };
                    return Ok(cachedResponse);
                }

                // --- Encolar el Trabajo ---
                var jobId = Guid.NewGuid().ToString();
                _queueService.EnqueueReportJob(jobId, jsonData, reportType, userName, true, mode, requestHash);
                _logger.LogInformation("Reporte encolado con JobId {JobId} para el usuario {UserName}, modo: {ProcessingMode}", jobId, userName, mode);
                
                var statusUrl = Url.Action(nameof(GetJobStatus), new { jobId });
                var response = new ApiResponse<QueueReportResponse>
                {
                    Success = true,
                    Data = new QueueReportResponse(jobId, dataFile.Length, mode, statusUrl)
                };

                return Accepted(statusUrl, response); // 202 Accepted es más apropiado para encolar
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "El archivo subido no contiene JSON válido.");
                return BadRequest(new ApiResponse<object> { Message = "El contenido del archivo no es JSON válido.", ErrorCode = "INVALID_JSON" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encolando reporte multipart.");
                return StatusCode(500, new ApiResponse<object> { Message = "Error interno del servidor.", ErrorCode = "QUEUE_ERROR" });
            }
        }

        [HttpGet("status/{jobId}")]
        [SwaggerOperation(Summary = "Obtiene el estado de un trabajo de reporte", OperationId = "GetJobStatus")]
        [ProducesResponseType(typeof(ApiResponse<JobStatusResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 404)]
        public IActionResult GetJobStatus(string jobId)
        {
            var status = _queueService.GetJobStatus(jobId);
            if (status == null)
            {
                return NotFound(new ApiResponse<object> { Message = "Trabajo no encontrado.", ErrorCode = "JOB_NOT_FOUND" });
            }

            // Centralizar la URL de descarga para que apunte al DownloadController
            var downloadUrl = status.Status == ReportJobStatusEnum.Completed && !string.IsNullOrEmpty(status.DownloadToken)
                ? GenerateDownloadUrl(status.DownloadToken)
                : null;
            
            var response = new ApiResponse<JobStatusResponse>
            {
                Success = true,
                Data = JobStatusResponse.FromJobInfo(status, downloadUrl)
            };

            return Ok(response);
        }

        [HttpGet("active-jobs")]
        [SwaggerOperation(Summary = "Obtiene la lista de trabajos activos", OperationId = "GetActiveJobs")]
        [ProducesResponseType(typeof(ApiResponse<ActiveJobsListResponse>), 200)]
        public IActionResult GetActiveJobs()
        {
            var activeJobs = _queueService.GetActiveJobs()
                .Select(job => ActiveJobSummary.FromJobInfo(job))
                .ToList();
            
            var response = new ApiResponse<ActiveJobsListResponse>
            {
                Success = true,
                Data = new ActiveJobsListResponse(activeJobs)
            };

            return Ok(response);
        }

        #region Métodos Privados y Helpers

        private string GenerateDownloadUrl(string token)
        {
            // Apunta al DownloadController centralizado en lugar de a un endpoint local.
            // Esto asume que tienes un DownloadController con una acción "DownloadFile".
            return Url.Action("DownloadFile", "Download", new { token }, Request.Scheme);
        }

        private string CreateRequestCacheHash(string reportType, string mode, string jsonData)
        {
            using var md5 = MD5.Create();
            var jsonHashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(jsonData));
            var combinedKey = $"{reportType}|{mode}|{Convert.ToHexString(jsonHashBytes)}";
            var requestHashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(combinedKey));
            return Convert.ToHexString(requestHashBytes).ToLowerInvariant();
        }

        private IActionResult? ValidateQueueRequest(IFormFile dataFile, string processingMode)
        {
            if (dataFile == null || dataFile.Length == 0)
                return BadRequest(new ApiResponse<object> { Message = "El archivo de datos es requerido.", ErrorCode = "FILE_REQUIRED" });

            if (!dataFile.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(dataFile.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new ApiResponse<object> { Message = "El archivo debe ser de tipo JSON.", ErrorCode = "INVALID_FILE_TYPE" });

            var mode = (processingMode ?? "batch").Trim().ToLowerInvariant();
            if (mode != "batch" && mode != "split")
                return BadRequest(new ApiResponse<object> { Message = "processingMode inválido. Valores permitidos: 'batch' o 'split'.", ErrorCode = "INVALID_MODE" });
            
            return null; // Sin errores
        }
        
        #endregion
    }

    #region DTOs (Data Transfer Objects)

    public record QueueReportResponse(string JobId, long DataSizeBytes, string ProcessingMode, string? StatusCheckUrl)
    {
        public string Message { get; } = "Reporte encolado para procesamiento asíncrono.";
        public string EstimatedProcessingTime { get; } = "Puede variar. Consulte la URL de estado para ver el progreso.";
    }

    public record CachedReportResponse(string FileName, long FileSizeBytes, string DownloadUrl, DateTimeOffset ExpiresAt, string MD5Hash)
    {
        public bool CachedHit { get; } = true;
        public string Message { get; } = "Reporte idéntico encontrado en caché. No se ha encolado un nuevo trabajo.";

        public CachedReportResponse(TemporaryFileInfo fileInfo, string downloadUrl) 
            : this(fileInfo.OriginalFileName, fileInfo.FileSizeBytes, downloadUrl, fileInfo.ExpiresAt, fileInfo.MD5Hash)
        {
        }
    }
    
    public record JobStatusResponse
    {
        public string JobId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Message { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string? FileName { get; init; }
        public long? FileSizeBytes { get; init; }
        public string? DownloadUrl { get; init; }
        public string ProcessingMode { get; init; } = string.Empty;
        public JobProgress? Progress { get; init; }
        public JobPerformanceMetrics? PerformanceMetrics { get; init; }

        public static JobStatusResponse FromJobInfo(ReportJobStatus jobInfo, string? downloadUrl) => new()
        {
            JobId = jobInfo.JobId,
            Status = jobInfo.Status.ToString(),
            Message = jobInfo.Message,
            CreatedAt = jobInfo.CreatedAt,
            CompletedAt = jobInfo.CompletedAt,
            FileName = jobInfo.FileName,
            FileSizeBytes = jobInfo.FileSizeBytes,
            DownloadUrl = downloadUrl,
            ProcessingMode = jobInfo.ProcessingMode,
            Progress = jobInfo.Status is ReportJobStatusEnum.Processing or ReportJobStatusEnum.Completed
                ? new JobProgress(jobInfo.ProcessedRecords, jobInfo.TotalRecords, (int)jobInfo.PercentComplete, jobInfo.CurrentRecordsPerSecond, jobInfo.StartedAt)
                : null,
            PerformanceMetrics = jobInfo.PerformanceMetrics != null
                ? new JobPerformanceMetrics(jobInfo.PerformanceMetrics.TotalExecutionTimeMs, jobInfo.PerformanceMetrics.RecordsProcessed, jobInfo.PerformanceMetrics.RecordsPerSecond)
                : null
        };
    }

    public record JobProgress(int ProcessedRecords, int TotalRecords, int Percent, double CurrentRps, DateTimeOffset? StartedAt);
    public record JobPerformanceMetrics(long ProcessingTimeMs, int RecordsProcessed, double RecordsPerSecond);

    public record ActiveJobSummary
    {
        public string JobId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string ProcessingMode { get; init; } = string.Empty;
        public int ProgressPercent { get; init; }
        public DateTimeOffset CreatedAt { get; init; }

        public static ActiveJobSummary FromJobInfo(ReportJobStatus jobInfo) => new()
        {
            JobId = jobInfo.JobId,
            Status = jobInfo.Status.ToString(),
            ProcessingMode = jobInfo.ProcessingMode,
            ProgressPercent = (int)jobInfo.PercentComplete,
            CreatedAt = jobInfo.CreatedAt
        };
    }
    
    public record ActiveJobsListResponse(IReadOnlyList<ActiveJobSummary> Jobs)
    {
        public int TotalActiveJobs => Jobs.Count;
    }

    #endregion
}