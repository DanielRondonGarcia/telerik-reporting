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
        /// Endpoint para generar reportes usando Telerik con datos JSON
        /// </summary>
        /// <param name="dataSource">Objeto JSON que contiene los datos del reporte</param>
        /// <param name="reportType">Tipo de reporte a generar (opcional, por defecto "USUARIO")</param>
        /// <param name="userName">Nombre del usuario que genera el reporte (opcional, por defecto "SYSTEM")</param>
        /// <returns>Archivo PDF generado</returns>
        /// <remarks>
        /// Ejemplo de request body:
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
        /// <response code="200">Reporte generado exitosamente</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpPost("telerik/json/file/batch")]
        [SwaggerOperation(
            Summary = "Generar reporte con datos JSON",
            Description = "Genera un reporte PDF usando Telerik con los datos JSON proporcionados. Acepta cualquier estructura JSON válida.",
            OperationId = "GenerateReport"
        )]
        [SwaggerResponse(200, "Reporte generado exitosamente", typeof(ApiResponse<UFile>))]
        [SwaggerResponse(500, "Error interno del servidor", typeof(ApiResponse<UFile>))]
        public IActionResult GenerateReport(
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

                // Generar el reporte
                fileOutput = reporteNegocio.ExecuteReport(jsonString, reportType, userName);

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
    }
}
