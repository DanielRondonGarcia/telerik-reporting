using System.Diagnostics;
using System.Text.Json;

namespace GenReports.Middleware
{
    /// <summary>
    /// Middleware para manejar requests grandes y timeouts de manera optimizada
    /// </summary>
    public class LargeRequestHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LargeRequestHandlingMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public LargeRequestHandlingMiddleware(
            RequestDelegate next, 
            ILogger<LargeRequestHandlingMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestPath = context.Request.Path.Value ?? "";
            var isAsyncReportRequest = requestPath.Contains("/AsyncReports/", StringComparison.OrdinalIgnoreCase);
            var isLargeRequest = context.Request.ContentLength > 10_000_000; // 10MB

            try
            {
                // Configurar timeout extendido para requests de reportes asíncronos
                if (isAsyncReportRequest)
                {
                    var timeoutMinutes = _configuration.GetValue<int>("AsyncReports:JobTimeoutMinutes", 60);
                    context.RequestAborted.Register(() =>
                    {
                        _logger.LogWarning($"Request cancelado: {requestPath} después de {stopwatch.ElapsedMilliseconds}ms");
                    });
                }

                // Log para requests grandes
                if (isLargeRequest)
                {
                    _logger.LogInformation($"Procesando request grande: {requestPath}, tamaño: {context.Request.ContentLength} bytes");
                }

                // Configurar headers para streaming si es necesario
                if (isAsyncReportRequest && context.Request.Method == "POST")
                {
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                }

                await _next(context);

                // Log de éxito
                stopwatch.Stop();
                if (isLargeRequest || stopwatch.ElapsedMilliseconds > 5000)
                {
                    _logger.LogInformation($"Request completado: {requestPath} en {stopwatch.ElapsedMilliseconds}ms");
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning($"Request cancelado por el cliente: {requestPath} después de {stopwatch.ElapsedMilliseconds}ms");
                
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 499; // Client Closed Request
                    await context.Response.WriteAsync("Request cancelado por el cliente");
                }
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, $"Datos inválidos en request: {requestPath}");
                await HandleErrorResponse(context, 400, "Datos de entrada inválidos", ex.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"Error de formato JSON en request: {requestPath}");
                await HandleErrorResponse(context, 400, "Formato JSON inválido", ex.Message);
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, $"Error de memoria insuficiente en request: {requestPath}");
                await HandleErrorResponse(context, 413, "Archivo demasiado grande", "El archivo excede la capacidad de procesamiento disponible");
            }
            catch (IOException ex) when (ex.Message.Contains("request body too large"))
            {
                _logger.LogError(ex, $"Request body demasiado grande: {requestPath}");
                await HandleErrorResponse(context, 413, "Archivo demasiado grande", "El tamaño del archivo excede el límite permitido");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error no manejado en request: {requestPath}");
                await HandleErrorResponse(context, 500, "Error interno del servidor", "Ocurrió un error inesperado");
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private async Task HandleErrorResponse(HttpContext context, int statusCode, string error, string details)
        {
            if (context.Response.HasStarted)
                return;

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var response = new
            {
                Error = error,
                Details = details,
                Timestamp = DateTime.UtcNow,
                RequestId = context.TraceIdentifier,
                Suggestions = GetSuggestions(statusCode)
            };

            var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(jsonResponse);
        }

        private static string[] GetSuggestions(int statusCode)
        {
            return statusCode switch
            {
                400 => new[]
                {
                    "Verifique que el JSON esté bien formateado",
                    "Asegúrese de que todos los campos requeridos estén presentes",
                    "Valide que los tipos de datos sean correctos"
                },
                413 => new[]
                {
                    "Use el endpoint /api/AsyncReports/queue-multipart para archivos grandes",
                    "Considere dividir los datos en lotes más pequeños",
                    "Comprima el archivo JSON antes de enviarlo"
                },
                499 => new[]
                {
                    "Use el endpoint asíncrono para reportes grandes",
                    "Implemente reintentos con backoff exponencial",
                    "Verifique la estabilidad de la conexión de red"
                },
                500 => new[]
                {
                    "Reintente la operación después de unos minutos",
                    "Contacte al administrador del sistema si el problema persiste",
                    "Use el endpoint asíncrono para mayor confiabilidad"
                },
                _ => new[]
                {
                    "Consulte la documentación de la API",
                    "Verifique los logs del servidor para más detalles"
                }
            };
        }
    }

    /// <summary>
    /// Extensión para registrar el middleware
    /// </summary>
    public static class LargeRequestHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseLargeRequestHandling(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LargeRequestHandlingMiddleware>();
        }
    }
}