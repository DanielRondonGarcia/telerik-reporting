using GenReports.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenReports.Services
{
    /// <summary>
    /// Servicio de caché de archivos temporales con validación MD5
    /// </summary>
    public class TemporaryFileCacheService : ITemporaryFileCacheService
    {
        private readonly TemporaryFileCacheOptions _options;
        private readonly ILogger<TemporaryFileCacheService> _logger;
        private readonly string _cacheDirectory;
        private readonly string _metadataFile;

        public TemporaryFileCacheService(IOptions<TemporaryFileCacheOptions> options, ILogger<TemporaryFileCacheService> logger)
        {
            _options = options.Value;
            _logger = logger;
            _cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), _options.CacheDirectory);
            _metadataFile = Path.Combine(_cacheDirectory, "cache_metadata.json");

            // Crear directorio de caché si no existe
            Directory.CreateDirectory(_cacheDirectory);
        }

        public async Task<TemporaryFileInfo> StoreFileAsync(byte[] fileBytes, string originalFileName, string contentType = "application/octet-stream")
        {
            var md5Hash = CalculateMD5Hash(fileBytes);
            return await StoreFileAsync(fileBytes, originalFileName, contentType, md5Hash);
        }

        public async Task<TemporaryFileInfo> StoreFileAsync(byte[] fileBytes, string originalFileName, string contentType, string customMD5Hash)
        {
            try
            {
                // Verificar si ya existe un archivo con el mismo hash
                if (_options.EnableMD5Validation)
                {
                    var existingFile = await FindByMD5HashAsync(customMD5Hash);
                    if (existingFile != null && File.Exists(existingFile.FilePath))
                    {
                        _logger.LogInformation($"Archivo encontrado en caché con MD5: {customMD5Hash}");
                        return existingFile;
                    }
                }

                // Crear nuevo archivo temporal
                var downloadToken = Guid.NewGuid().ToString("N");
                var fileName = $"{downloadToken}_{Path.GetFileName(originalFileName)}";
                var filePath = Path.Combine(_cacheDirectory, fileName);

                // Guardar archivo
                await File.WriteAllBytesAsync(filePath, fileBytes);

                var fileInfo = new TemporaryFileInfo
                {
                    MD5Hash = customMD5Hash,
                    FilePath = filePath,
                    OriginalFileName = originalFileName,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(_options.ExpirationTimeMinutes),
                    FileSizeBytes = fileBytes.Length,
                    DownloadToken = downloadToken,
                    ContentType = contentType
                };

                // Guardar metadata
                await SaveFileMetadataAsync(fileInfo);

                _logger.LogInformation($"Archivo almacenado en caché: {originalFileName}, Token: {downloadToken}, MD5: {customMD5Hash}");
                return fileInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al almacenar archivo en caché: {originalFileName}");
                throw;
            }
        }

        public async Task<TemporaryFileInfo?> GetFileInfoAsync(string downloadToken)
        {
            try
            {
                var metadata = await LoadCacheMetadataAsync();
                var fileInfo = metadata.FirstOrDefault(f => f.DownloadToken == downloadToken);

                if (fileInfo == null || DateTime.UtcNow > fileInfo.ExpiresAt)
                {
                    return null;
                }

                if (!File.Exists(fileInfo.FilePath))
                {
                    _logger.LogWarning($"Archivo no encontrado en disco: {fileInfo.FilePath}");
                    return null;
                }

                return fileInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener información del archivo: {downloadToken}");
                return null;
            }
        }

        public async Task<byte[]?> GetFileContentAsync(string downloadToken)
        {
            try
            {
                var fileInfo = await GetFileInfoAsync(downloadToken);
                if (fileInfo == null)
                {
                    return null;
                }

                return await File.ReadAllBytesAsync(fileInfo.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al leer contenido del archivo: {downloadToken}");
                return null;
            }
        }

        public async Task<TemporaryFileInfo?> FindByMD5HashAsync(string md5Hash)
        {
            try
            {
                var metadata = await LoadCacheMetadataAsync();
                var fileInfo = metadata.FirstOrDefault(f => f.MD5Hash == md5Hash && DateTime.UtcNow <= f.ExpiresAt);

                if (fileInfo != null && !File.Exists(fileInfo.FilePath))
                {
                    _logger.LogWarning($"Archivo con MD5 {md5Hash} no encontrado en disco: {fileInfo.FilePath}");
                    return null;
                }

                return fileInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al buscar archivo por MD5: {md5Hash}");
                return null;
            }
        }

        public async Task<CleanupResult> CleanupExpiredFilesAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new CleanupResult();

            try
            {
                var metadata = await LoadCacheMetadataAsync();
                var expiredFiles = metadata.Where(f => DateTime.UtcNow > f.ExpiresAt).ToList();

                foreach (var expiredFile in expiredFiles)
                {
                    try
                    {
                        if (File.Exists(expiredFile.FilePath))
                        {
                            var fileInfo = new FileInfo(expiredFile.FilePath);
                            var fileSize = fileInfo.Length;
                            
                            File.Delete(expiredFile.FilePath);
                            result.FilesDeleted++;
                            result.SpaceFreedBytes += fileSize;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error al eliminar archivo expirado: {expiredFile.FilePath}");
                        result.FailedDeletions.Add(expiredFile.FilePath);
                    }
                }

                // Actualizar metadata sin archivos expirados
                var validMetadata = metadata.Where(f => DateTime.UtcNow <= f.ExpiresAt).ToList();
                await SaveCacheMetadataAsync(validMetadata);

                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                if (result.FilesDeleted > 0)
                {
                    _logger.LogInformation($"Limpieza completada: {result.FilesDeleted} archivos eliminados, {result.SpaceFreedMB} MB liberados");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la limpieza de archivos expirados");
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                return result;
            }
        }

        public string GenerateDownloadUrl(string downloadToken, string baseUrl)
        {
            return $"{baseUrl.TrimEnd('/')}/api/download/{downloadToken}";
        }

        private string CalculateMD5Hash(byte[] data)
        {
            using var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(data);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private async Task<List<TemporaryFileInfo>> LoadCacheMetadataAsync()
        {
            try
            {
                if (!File.Exists(_metadataFile))
                {
                    return new List<TemporaryFileInfo>();
                }

                var json = await File.ReadAllTextAsync(_metadataFile);
                return JsonSerializer.Deserialize<List<TemporaryFileInfo>>(json) ?? new List<TemporaryFileInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al cargar metadata del caché, creando nueva lista");
                return new List<TemporaryFileInfo>();
            }
        }

        private async Task SaveCacheMetadataAsync(List<TemporaryFileInfo> metadata)
        {
            try
            {
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_metadataFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar metadata del caché");
            }
        }

        private async Task SaveFileMetadataAsync(TemporaryFileInfo fileInfo)
        {
            var metadata = await LoadCacheMetadataAsync();
            metadata.Add(fileInfo);
            await SaveCacheMetadataAsync(metadata);
        }

        public async Task<bool> ValidateMD5HashAsync(string filePath, string expectedHash)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var actualHash = CalculateMD5Hash(fileBytes);
                return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al validar hash MD5 del archivo {FilePath}", filePath);
                return false;
            }
        }
    }
}