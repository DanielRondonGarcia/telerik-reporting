using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenReports.Services
{
    /// <summary>
    /// Implementación del servicio de claves de caché globales basadas en contenido
    /// </summary>
    public class GlobalCacheKeyService : IGlobalCacheKeyService
    {
        private readonly ILogger<GlobalCacheKeyService> _logger;

        public GlobalCacheKeyService(ILogger<GlobalCacheKeyService> logger)
        {
            _logger = logger;
        }

        public string GenerateContentBasedCacheKey(string jsonContent)
        {
            var normalizedJson = NormalizeJsonContent(jsonContent);
            return CalculateMD5Hash(normalizedJson);
        }

        public string GenerateReportTypeCacheKey(string jsonContent, string reportType)
        {
            var normalizedJson = NormalizeJsonContent(jsonContent);
            var combinedContent = $"{normalizedJson}|{reportType.ToLowerInvariant()}";
            return CalculateMD5Hash(combinedContent);
        }

        public string NormalizeJsonContent(string jsonContent)
        {
            try
            {
                // Parsear y re-serializar el JSON para normalizar el formato
                // Esto elimina espacios en blanco inconsistentes y ordena las propiedades
                var jsonDocument = JsonDocument.Parse(jsonContent);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = false, // Sin indentación para consistencia
                    PropertyNamingPolicy = null, // Mantener nombres originales
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var normalizedJson = JsonSerializer.Serialize(jsonDocument, options);
                
                _logger.LogDebug("JSON normalizado para caché: {NormalizedLength} caracteres", normalizedJson.Length);
                return normalizedJson;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error al normalizar JSON, usando contenido original");
                // Si hay error en el parsing, usar el contenido original
                return jsonContent.Trim();
            }
        }

        private string CalculateMD5Hash(string input)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}