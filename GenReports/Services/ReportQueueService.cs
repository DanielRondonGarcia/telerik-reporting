using System.Collections.Concurrent;
using System.Text.Json;
using GenReports.business;
using GenReports.Models;

namespace GenReports.Services
{
    /// <summary>
    /// Servicio para manejar cola asíncrona de reportes grandes
    /// </summary>
    public class ReportQueueService : BackgroundService
    {
        private readonly ConcurrentQueue<ReportJob> _reportQueue = new();
        private readonly ConcurrentDictionary<string, ReportJobStatus> _jobStatuses = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReportQueueService> _logger;
        private readonly SemaphoreSlim _semaphore;

        public ReportQueueService(IServiceProvider serviceProvider, ILogger<ReportQueueService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            // Limitar a 2 trabajos concurrentes para evitar sobrecarga
            _semaphore = new SemaphoreSlim(2, 2);
        }

        /// <summary>
        /// Encola un trabajo de reporte para procesamiento asíncrono
        /// </summary>
        /// <param name="jobId">ID único del trabajo</param>
        /// <param name="jsonData">Datos JSON para el reporte</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Usuario que solicita el reporte</param>
        /// <param name="isLargeDataset">Indica si es un dataset grande que requiere streaming</param>
        /// <returns>ID del trabajo encolado</returns>
        public string EnqueueReportJob(string jobId, string jsonData, string reportType, string userName, bool isLargeDataset = false)
        {
            var job = new ReportJob
            {
                JobId = jobId,
                JsonData = jsonData,
                ReportType = reportType,
                UserName = userName,
                IsLargeDataset = isLargeDataset,
                CreatedAt = DateTime.UtcNow,
                Status = ReportJobStatusEnum.Queued
            };

            _reportQueue.Enqueue(job);
            
            var status = new ReportJobStatus
            {
                JobId = jobId,
                Status = ReportJobStatusEnum.Queued,
                CreatedAt = DateTime.UtcNow,
                Message = "Trabajo encolado para procesamiento"
            };

            _jobStatuses.TryAdd(jobId, status);
            
            _logger.LogInformation($"Trabajo de reporte encolado: {jobId} para usuario {userName}");
            
            return jobId;
        }

        /// <summary>
        /// Obtiene el estado de un trabajo
        /// </summary>
        /// <param name="jobId">ID del trabajo</param>
        /// <returns>Estado del trabajo o null si no existe</returns>
        public ReportJobStatus? GetJobStatus(string jobId)
        {
            _jobStatuses.TryGetValue(jobId, out var status);
            return status;
        }

        /// <summary>
        /// Obtiene todos los trabajos activos
        /// </summary>
        /// <returns>Lista de estados de trabajos</returns>
        public IEnumerable<ReportJobStatus> GetActiveJobs()
        {
            return _jobStatuses.Values
                .Where(s => s.Status != ReportJobStatusEnum.Completed && s.Status != ReportJobStatusEnum.Failed)
                .OrderBy(s => s.CreatedAt);
        }

        /// <summary>
        /// Limpia trabajos completados o fallidos antiguos
        /// </summary>
        public void CleanupOldJobs(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.UtcNow - maxAge;
            var jobsToRemove = _jobStatuses.Values
                .Where(s => (s.Status == ReportJobStatusEnum.Completed || s.Status == ReportJobStatusEnum.Failed) 
                           && s.CreatedAt < cutoffTime)
                .Select(s => s.JobId)
                .ToList();

            foreach (var jobId in jobsToRemove)
            {
                _jobStatuses.TryRemove(jobId, out _);
                _logger.LogInformation($"Trabajo limpiado: {jobId}");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReportQueueService iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_reportQueue.TryDequeue(out var job))
                    {
                        // Esperar por un slot disponible
                        await _semaphore.WaitAsync(stoppingToken);
                        
                        // Procesar el trabajo en paralelo
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessReportJob(job);
                            }
                            finally
                            {
                                _semaphore.Release();
                            }
                        }, stoppingToken);
                    }
                    else
                    {
                        // No hay trabajos, esperar un poco
                        await Task.Delay(1000, stoppingToken);
                    }

                    // Limpiar trabajos antiguos cada 5 minutos
                    if (DateTime.UtcNow.Minute % 5 == 0)
                    {
                        CleanupOldJobs(TimeSpan.FromHours(2));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el bucle principal de ReportQueueService");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("ReportQueueService detenido");
        }

        private async Task ProcessReportJob(ReportJob job)
        {
            try
            {
                _logger.LogInformation($"Iniciando procesamiento del trabajo: {job.JobId}");
                
                // Actualizar estado a procesando
                UpdateJobStatus(job.JobId, ReportJobStatusEnum.Processing, "Procesando reporte...");

                using var scope = _serviceProvider.CreateScope();
                var reportService = scope.ServiceProvider.GetRequiredService<Report>();
                var cacheService = scope.ServiceProvider.GetRequiredService<ITemporaryFileCacheService>();

                ArchivoResult fileOutput;
                ReportPerformanceMetrics metricas;

                // Determinar si usar procesamiento streaming
                if (job.IsLargeDataset)
                {
                    _logger.LogInformation($"Usando procesamiento streaming para trabajo: {job.JobId}");
                    
                    using var jsonStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(job.JsonData));
                    using var reportServiceScope = _serviceProvider.CreateScope();
                    var reportServiceInstance = reportServiceScope.ServiceProvider.GetRequiredService<Report>();
                    
                    // Aquí necesitaríamos adaptar el método para usar streaming directamente
                    // Por ahora usamos el método estándar
                    fileOutput = await reportService.ExecuteBatchReportsCompressed(job.JsonData, job.ReportType, job.UserName);
                    metricas = null; // TODO: Implementar métricas para procesamiento streaming
                }
                else
                {
                    _logger.LogInformation($"Usando procesamiento estándar para trabajo: {job.JobId}");
                    fileOutput = await reportService.ExecuteBatchReportsCompressed(job.JsonData, job.ReportType, job.UserName);
                    metricas = null; // TODO: Implementar métricas para procesamiento estándar
                }

                if (fileOutput?.BytesArchivo != null)
                {
                    // Guardar archivo en caché temporal
                    var contentType = fileOutput.NombreArchivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "application/zip"
                        : "application/pdf";

                    var tempFileInfo = await cacheService.StoreFileAsync(
                        fileOutput.BytesArchivo,
                        fileOutput.NombreArchivo,
                        contentType,
                        job.JobId // Usar jobId como hash único
                    );

                    // Actualizar estado a completado con información del archivo
                    var completedStatus = new ReportJobStatus
                    {
                        JobId = job.JobId,
                        Status = ReportJobStatusEnum.Completed,
                        CreatedAt = job.CreatedAt,
                        CompletedAt = DateTime.UtcNow,
                        Message = "Reporte generado exitosamente",
                        FileName = fileOutput.NombreArchivo,
                        DownloadToken = tempFileInfo.DownloadToken,
                        FileSizeBytes = fileOutput.BytesArchivo.Length,
                        PerformanceMetrics = metricas
                    };

                    _jobStatuses.TryUpdate(job.JobId, completedStatus, _jobStatuses[job.JobId]);
                    
                    _logger.LogInformation($"Trabajo completado exitosamente: {job.JobId}");
                }
                else
                {
                    throw new InvalidOperationException("El reporte generado está vacío");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error procesando trabajo: {job.JobId}");
                
                UpdateJobStatus(job.JobId, ReportJobStatusEnum.Failed, $"Error: {ex.Message}");
            }
        }

        private void UpdateJobStatus(string jobId, ReportJobStatusEnum status, string message)
        {
            if (_jobStatuses.TryGetValue(jobId, out var currentStatus))
            {
                var updatedStatus = new ReportJobStatus
                {
                    JobId = jobId,
                    Status = status,
                    CreatedAt = currentStatus.CreatedAt,
                    CompletedAt = status == ReportJobStatusEnum.Completed || status == ReportJobStatusEnum.Failed 
                        ? DateTime.UtcNow 
                        : currentStatus.CompletedAt,
                    Message = message,
                    FileName = currentStatus.FileName,
                    DownloadToken = currentStatus.DownloadToken,
                    FileSizeBytes = currentStatus.FileSizeBytes,
                    PerformanceMetrics = currentStatus.PerformanceMetrics
                };

                _jobStatuses.TryUpdate(jobId, updatedStatus, currentStatus);
            }
        }
    }

    /// <summary>
    /// Representa un trabajo de reporte en la cola
    /// </summary>
    public class ReportJob
    {
        public string JobId { get; set; } = string.Empty;
        public string JsonData { get; set; } = string.Empty;
        public string ReportType { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public bool IsLargeDataset { get; set; }
        public DateTime CreatedAt { get; set; }
        public ReportJobStatusEnum Status { get; set; }
    }

    /// <summary>
    /// Estado de un trabajo de reporte
    /// </summary>
    public class ReportJobStatus
    {
        public string JobId { get; set; } = string.Empty;
        public ReportJobStatusEnum Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? DownloadToken { get; set; }
        public long? FileSizeBytes { get; set; }
        public ReportPerformanceMetrics? PerformanceMetrics { get; set; }
    }

    /// <summary>
    /// Estados posibles de un trabajo de reporte
    /// </summary>
    public enum ReportJobStatusEnum
    {
        Queued,
        Processing,
        Completed,
        Failed
    }
}