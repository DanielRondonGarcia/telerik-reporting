using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using GenReports.business;
using GenReports.Models;
using GenReports.Helpers;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace GenReports.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
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
        [SwaggerResponse(200, "Reporte(s) generado(s) exitosamente", typeof(ApiResponse<UFile>))]
        [SwaggerResponse(500, "Error interno del servidor", typeof(ApiResponse<UFile>))]
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
            ArchivoResult? fileOutput = null;
            
            try
            {
                Console.WriteLine($"Inicio del telerik/json/file/batch : {DateTime.Now}");
                var stopwatch = Stopwatch.StartNew();

                // Convertir el objeto a JSON string
                var jsonString = JsonSerializer.Serialize(dataSource);
                Console.WriteLine($"JSON recibido: {jsonString}");

                // Crear instancia del negocio de reportes
                var reporteNegocio = new Report();

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

                stopwatch.Stop();
                Console.WriteLine($"Tiempo total del telerik/json/file/batch: {DateTime.Now} -> {stopwatch.Elapsed}");

                // Crear la respuesta de la API
                var apiResponse = new ApiResponse<UFile>
                {
                    Data = fileOutput == null ? null : new ConvertModels().ConvertToFile(fileOutput),
                    Success = fileOutput != null,
                    Message = fileOutput != null ? "Reporte generado exitosamente" : "No se pudo generar el reporte"
                };

                return Ok(apiResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en telerik/json/file/batch: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                var errorResponse = new ApiResponse<UFile>
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

                // Verificar que existe la propiedad "Data"
                if (!root.TryGetProperty("Data", out var dataElement))
                {
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
    }
}
