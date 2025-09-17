using Microsoft.Extensions.Options;
using GenReports.Models;

namespace GenReports.Services
{
    /// <summary>
    /// Servicio de background que se ejecuta periódicamente para limpiar archivos expirados del caché temporal
    /// </summary>
    public class TemporaryFileCacheCleanupService : BackgroundService
    {
        private readonly ITemporaryFileCacheService _cacheService;
        private readonly TemporaryFileCacheOptions _options;
        private readonly ILogger<TemporaryFileCacheCleanupService> _logger;

        public TemporaryFileCacheCleanupService(
            ITemporaryFileCacheService cacheService,
            IOptions<TemporaryFileCacheOptions> options,
            ILogger<TemporaryFileCacheCleanupService> logger)
        {
            _cacheService = cacheService;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de limpieza de caché temporal iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Iniciando limpieza de archivos expirados...");
                    
                    var cleanupResult = await _cacheService.CleanupExpiredFilesAsync();
                    
                    if (cleanupResult.FilesDeleted > 0)
                    {
                        _logger.LogInformation(
                            "Limpieza completada: {FilesDeleted} archivos eliminados, {SpaceFreed} MB liberados",
                            cleanupResult.FilesDeleted,
                            Math.Round(cleanupResult.SpaceFreedBytes / (1024.0 * 1024.0), 2));
                    }
                    else
                    {
                        _logger.LogDebug("Limpieza completada: No se encontraron archivos expirados");
                    }

                    // Esperar el intervalo configurado antes de la próxima limpieza
                    var delayMinutes = _options.CleanupIntervalMinutes;
                    _logger.LogDebug("Próxima limpieza en {DelayMinutes} minutos", delayMinutes);
                    
                    await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancelación normal del servicio
                    _logger.LogInformation("Servicio de limpieza de caché temporal cancelado");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error durante la limpieza de archivos expirados");
                    
                    // En caso de error, esperar un tiempo menor antes de reintentar
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("Servicio de limpieza de caché temporal detenido");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Deteniendo servicio de limpieza de caché temporal...");
            await base.StopAsync(cancellationToken);
        }
    }
}