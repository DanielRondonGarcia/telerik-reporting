using GenReports.Models;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;
using Telerik.Reporting;
using Telerik.Reporting.Processing;

namespace GenReports.business
{
    public class Report
    {
        private readonly string _directorioTemporal;
        private readonly string _urlBaseReportes;
        private readonly ReportsConfiguration _reportsConfig;

        public Report(IOptions<ReportsConfiguration> reportsConfig, string urlBaseReportes = "")
        {
            _reportsConfig = reportsConfig.Value ?? throw new ArgumentNullException(nameof(reportsConfig));
            _directorioTemporal = _reportsConfig.TemporaryDirectory;
            _urlBaseReportes = urlBaseReportes;
        }

        /// <summary>
        /// Método principal que ejecuta un reporte de acuerdo al tipo del mismo
        /// </summary>
        /// <param name="reportJson">JSON que contiene la información del reporte con la estructura: { "Data": [...] }</param>
        /// <param name="reportType">Tipo de reporte a generar</param>
        /// <param name="userName">Nombre del usuario que genera el reporte</param>
        /// <returns>Archivo generado con el reporte</returns>
        public ArchivoResult ExecuteReport(string reportJson, string reportType, string userName = "SYSTEM")
        {
            try
            {
                // Validar que el JSON no esté vacío
                if (string.IsNullOrWhiteSpace(reportJson))
                {
                    throw new ArgumentException("El JSON del reporte no puede estar vacío", nameof(reportJson));
                }

                // Parsear el JSON para extraer la data
                var reportData = ExtractDataFromJson(reportJson);

                if (reportData == null || !reportData.Any())
                {
                    throw new InvalidOperationException("No se encontraron datos en el JSON del reporte");
                }

                // Generar el reporte usando Telerik
                var archivo = GenerateTelerik(reportData, reportType, userName);

                return archivo;
            }
            catch (Exception ex)
            {
                // Log del error (aquí puedes usar tu sistema de logging preferido)
                Console.WriteLine($"Error ejecutando reporte: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Mapea el tipo de reporte al nombre del archivo .trdp correspondiente
        /// </summary>
        /// <param name="reportType">Tipo de reporte</param>
        /// <returns>Nombre del archivo .trdp</returns>
        private string GetReportTemplateFileName(string reportType)
        {
            return reportType?.ToUpper() switch
            {
                "USUARIO" => "GEN_INFO_USUARIO_T.json.batch.trdp",
                "USUARIO_MASIVO" => "GEN_INFO_USUARIO_MASIVO_T.trdp",
                _ => throw new ArgumentException($"Tipo de reporte no soportado: {reportType}")
            };
        }

        /// <summary>
        /// Extrae los datos del JSON del reporte
        /// </summary>
        /// <param name="reportJson">JSON con la estructura { "Data": [...] }</param>
        /// <returns>Lista de objetos extraídos de la propiedad Data</returns>
        private List<object> ExtractDataFromJson(string reportJson)
        {
            try
            {
                using var document = JsonDocument.Parse(reportJson);
                var root = document.RootElement;

                // Verificar que existe la propiedad "Data"
                if (!root.TryGetProperty("Data", out var dataElement))
                {
                    throw new InvalidOperationException("El JSON debe contener una propiedad 'Data'");
                }

                // Convertir la data a una lista de objetos
                var dataList = new List<object>();

                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        // Convertir cada elemento a un objeto dinámico
                        var itemObject = JsonSerializer.Deserialize<object>(item.GetRawText());
                        if (itemObject != null)
                        {
                            dataList.Add(itemObject);
                        }
                    }
                }
                else
                {
                    // Si Data no es un array, agregar el objeto único
                    var singleObject = JsonSerializer.Deserialize<object>(dataElement.GetRawText());
                    if (singleObject != null)
                    {
                        dataList.Add(singleObject);
                    }
                }

                return dataList;
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Error parseando el JSON: {ex.Message}", nameof(reportJson));
            }
        }

        /// <summary>
        /// Genera el reporte usando Telerik Reporting
        /// </summary>
        /// <param name="reportData">Datos del reporte</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Nombre del usuario</param>
        /// <returns>Archivo generado</returns>
        private ArchivoResult GenerateTelerik(List<object> reportData, string reportType, string userName)
        {
            try
            {
                // Mapear el tipo de reporte al archivo .trdp correspondiente
                var plantillaFileName = GetReportTemplateFileName(reportType);
                var plantillaPath = Path.Combine(_reportsConfig.BasePath, plantillaFileName);

                Console.WriteLine($"Buscando plantilla para reportType '{reportType}': {plantillaPath}");

                // Verificar que existe la plantilla
                if (!File.Exists(plantillaPath))
                {
                    // Listar archivos disponibles para debugging
                    var availableFiles = Directory.Exists(_reportsConfig.BasePath)
                        ? Directory.GetFiles(_reportsConfig.BasePath, "*.trdp")
                        : new string[0];

                    var availableList = string.Join(", ", availableFiles.Select(Path.GetFileName));
                    throw new FileNotFoundException($"No se encuentra el archivo de plantilla del reporte: {plantillaPath}. Archivos disponibles: {availableList}");
                }

                // Crear el procesador de reportes
                var telerikReportProcessor = new ReportProcessor();

                // Configurar información del dispositivo
                var deviceInfo = new System.Collections.Hashtable();

                // Cargar la definición del reporte desde el archivo .trdp
                var reportPackager = new ReportPackager();
                Telerik.Reporting.Report reportDefinition;

                try
                {
                    using (var fs = new FileStream(plantillaPath, FileMode.Open, FileAccess.Read))
                    {
                        // Intentar cargar el reporte con manejo específico para problemas de serialización
                        var document = reportPackager.UnpackageDocument(fs);
                        reportDefinition = document as Telerik.Reporting.Report;

                        if (reportDefinition == null)
                        {
                            throw new InvalidOperationException($"El archivo {plantillaPath} no contiene un reporte válido de Telerik");
                        }
                    }
                    Console.WriteLine($"Reporte cargado exitosamente desde: {plantillaPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error cargando el reporte desde {plantillaPath}: {ex.Message}");
                    Console.WriteLine($"Tipo de excepción: {ex.GetType().Name}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");

                    // Si es un error de serialización, proporcionar más información
                    if (ex.Message.Contains("ReportSerializable") || ex.Message.Contains("serialization"))
                    {
                        throw new InvalidOperationException($"Error de serialización al cargar la plantilla del reporte. " +
                            $"Esto puede deberse a incompatibilidad entre la versión de Telerik usada para crear el archivo .trdp " +
                            $"y la versión actual, o problemas de compatibilidad entre Windows/Linux. " +
                            $"Archivo: {plantillaPath}. Error original: {ex.Message}", ex);
                    }

                    throw new InvalidOperationException($"Error cargando la plantilla del reporte: {ex.Message}", ex);
                }

                // Configurar el origen de datos
                ConfigureDataSource(reportDefinition, reportData);

                // Crear el InstanceReportSource
                var instanceReportSource = new InstanceReportSource
                {
                    ReportDocument = reportDefinition
                };

                // Agregar parámetros al reporte
                AddReportParameters(instanceReportSource, reportType, userName);

                // Renderizar el reporte
                var formatoSalida = "PDF";
                RenderingResult resultado;

                try
                {
                    Console.WriteLine("Iniciando procesamiento del reporte...");
                    resultado = telerikReportProcessor.RenderReport(formatoSalida, instanceReportSource, deviceInfo);
                    Console.WriteLine($"Reporte procesado. Tiene errores: {resultado.HasErrors}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error durante el procesamiento del reporte: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw new InvalidOperationException($"Error durante el procesamiento del reporte: {ex.Message}", ex);
                }

                // Verificar errores
                if (resultado.HasErrors)
                {
                    var errores = string.Join("; ", resultado.Errors.Select(e => e.Message));
                    Console.WriteLine($"El reporte tiene errores: {errores}");

                    if (resultado.DocumentBytes == null)
                    {
                        throw new InvalidOperationException($"Error generando el reporte: {errores}");
                    }
                }

                // Devolver el archivo como PDF
                var nombreArchivo = $"Reporte_{reportType}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                return new ArchivoResult
                {
                    BytesArchivo = resultado.DocumentBytes,
                    NombreArchivo = nombreArchivo
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generando el reporte con Telerik: {ex.Message}");
                throw;
            }
        }

        private void ConfigureDataSource(Telerik.Reporting.Report report, List<object> reportData)
        {
            try
            {
                // Buscar todos los objetos JsonDataSource en el reporte
                var jsonDataSources = FindJsonDataSources(report);

                if (jsonDataSources.Count == 0)
                {
                    Console.WriteLine("No se encontraron JsonDataSource en el reporte. Se intentará agregar uno automáticamente.");

                    // Crear un JsonDataSource básico si no existe
                    var dataSource = new Telerik.Reporting.JsonDataSource();

                    // Convertir los datos en un JSON que represente un array
                    var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
                    var jsonArray = JsonSerializer.Serialize(reportData, jsonOptions);

                    // Asignar el contenido JSON como Source (API correcta)
                    dataSource.Source = jsonArray;

                    // Asignar el JsonDataSource al reporte
                    report.DataSource = dataSource;
                    Console.WriteLine("Se agregó automáticamente un JsonDataSource al reporte");
                }
                else
                {
                    Console.WriteLine($"Se encontraron {jsonDataSources.Count} JsonDataSource en el reporte. Configurando...");

                    // Configurar cada JsonDataSource con los datos proporcionados
                    foreach (var dataSource in jsonDataSources)
                    {
                        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
                        var jsonArray = JsonSerializer.Serialize(reportData, jsonOptions);
                        // Asignar el contenido JSON como Source (API correcta)
                        dataSource.Source = jsonArray;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configurando el origen de datos: {ex.Message}");
                throw;
            }
        }

        // Helper: Busca de forma recursiva todos los JsonDataSource en el reporte y en los data items anidados
        private List<Telerik.Reporting.JsonDataSource> FindJsonDataSources(Telerik.Reporting.Report report)
        {
            var result = new List<Telerik.Reporting.JsonDataSource>();

            try
            {
                // Incluir el DataSource del propio reporte si corresponde
                if (report.DataSource is Telerik.Reporting.JsonDataSource jds)
                {
                    result.Add(jds);
                }

                // Recorrido recursivo por los items del reporte
                void ScanItem(Telerik.Reporting.ReportItemBase item)
                {
                    if (item is Telerik.Reporting.DataItem di)
                    {
                        if (di.DataSource is Telerik.Reporting.JsonDataSource jds2)
                        {
                            result.Add(jds2);
                        }
                    }

                    // Recorrer hijos si existen
                    if (item.Items != null)
                    {
                        foreach (var child in item.Items)
                        {
                            ScanItem(child);
                        }
                    }
                }

                foreach (var item in report.Items)
                {
                    ScanItem(item);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Advertencia en FindJsonDataSources: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Agrega parámetros al reporte
        /// </summary>
        /// <param name="instanceReportSource">Instancia del reporte</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Nombre del usuario</param>
        private void AddReportParameters(InstanceReportSource instanceReportSource, string reportType, string userName)
        {
            instanceReportSource.Parameters.Add("nombreReporte", reportType);
            instanceReportSource.Parameters.Add("versionReporte", "v1.0");
            instanceReportSource.Parameters.Add("userSistema", userName);
            instanceReportSource.Parameters.Add("userNombre", userName);
            instanceReportSource.Parameters.Add("tituloReporte", $"Reporte {reportType}");
            instanceReportSource.Parameters.Add("fechaGeneracion", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
        }

        /// <summary>
        /// Ejecuta reportes masivos generando un archivo individual por cada registro y los comprime en un ZIP
        /// </summary>
        /// <param name="reportJson">JSON que contiene la información del reporte con la estructura: { "Data": [...] }</param>
        /// <param name="reportType">Tipo de reporte a generar</param>
        /// <param name="userName">Nombre del usuario que genera el reporte</param>
        /// <returns>Archivo ZIP comprimido con todos los reportes individuales</returns>
        public async Task<ArchivoResult> ExecuteBatchReportsCompressed(string reportJson, string reportType, string userName = "SYSTEM")
        {
            try
            {
                Console.WriteLine($"Inicio de ExecuteBatchReportsCompressed: {DateTime.Now}");

                // Validar que el JSON no esté vacío
                if (string.IsNullOrWhiteSpace(reportJson))
                {
                    throw new ArgumentException("El JSON del reporte no puede estar vacío", nameof(reportJson));
                }

                // Parsear el JSON para extraer la data
                var reportData = ExtractDataFromJson(reportJson);

                if (reportData == null || !reportData.Any())
                {
                    throw new InvalidOperationException("No se encontraron datos en el JSON del reporte");
                }

                Console.WriteLine($"Procesando {reportData.Count} registros para reportes individuales");

                // Crear un stream en memoria para el archivo ZIP
                using var zipStream = new MemoryStream();
                using (var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    // Generar un reporte individual para cada registro
                    for (int i = 0; i < reportData.Count; i++)
                    {
                        try
                        {
                            Console.WriteLine($"Generando reporte {i + 1} de {reportData.Count}");

                            // Crear un JSON con un solo registro
                            var singleRecordData = new List<object> { reportData[i] };
                            var singleRecordJson = JsonSerializer.Serialize(new { Data = singleRecordData });

                            // Generar el reporte individual
                            var reporteIndividual = GenerateTelerik(singleRecordData, reportType, userName);

                            if (reporteIndividual?.BytesArchivo != null)
                            {
                                // Crear nombre único para el archivo dentro del ZIP
                                var nombreArchivo = $"{reportType}_Registro_{i + 1:D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                                // Agregar el archivo al ZIP
                                var zipEntry = zipArchive.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
                                using var entryStream = zipEntry.Open();
                                await entryStream.WriteAsync(reporteIndividual.BytesArchivo, 0, reporteIndividual.BytesArchivo.Length);

                                Console.WriteLine($"Archivo {nombreArchivo} agregado al ZIP");
                            }
                            else
                            {
                                Console.WriteLine($"Error: No se pudo generar el reporte para el registro {i + 1}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error generando reporte individual {i + 1}: {ex.Message}");
                            // Continuar con el siguiente registro en caso de error
                        }
                    }
                }

                // Crear el archivo de salida comprimido con nombre simple (sin ruta completa)
                var nombreArchivoZip = $"Reportes_{reportType}_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

                var archivoComprimido = new ArchivoResult
                {
                    NombreArchivo = nombreArchivoZip,
                    BytesArchivo = zipStream.ToArray(),
                    Usuario = userName,
                    FechaGeneracion = DateTime.Now
                };

                Console.WriteLine($"Archivo ZIP generado exitosamente: {nombreArchivoZip}, Tamaño: {archivoComprimido.BytesArchivo.Length} bytes");

                return archivoComprimido;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ExecuteBatchReportsCompressed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Ejecuta un reporte consolidado con todos los registros y luego lo divide en archivos individuales usando split
        /// </summary>
        /// <param name="jsonString">JSON con los datos para el reporte</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Usuario que genera el reporte</param>
        /// <returns>ArchivoResult con el archivo ZIP que contiene los PDFs divididos</returns>
        public async Task<ArchivoResult> ExecuteConsolidatedReportWithSplit(string jsonString, string reportType, string userName = "SYSTEM")
        {
            try
            {
                Console.WriteLine($"Inicio de ExecuteConsolidatedReportWithSplit: {DateTime.Now}");

                // Validar que el JSON no esté vacío
                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    throw new ArgumentException("El JSON del reporte no puede estar vacío", nameof(jsonString));
                }

                // Parsear el JSON para extraer la data
                var reportData = ExtractDataFromJson(jsonString);

                if (reportData == null || !reportData.Any())
                {
                    throw new InvalidOperationException("No se encontraron datos en el JSON del reporte");
                }

                Console.WriteLine($"Generando reporte consolidado con {reportData.Count} registros");

                // PASO 1: Generar un PDF consolidado reutilizando GenerateConsolidatedReport
                var reporteConsolidado = GenerateConsolidatedReport(jsonString, reportType, userName);

                if (reporteConsolidado?.BytesArchivo == null)
                {
                    throw new InvalidOperationException("No se pudo generar el reporte consolidado");
                }

                Console.WriteLine($"PDF consolidado generado exitosamente. Tamaño: {reporteConsolidado.BytesArchivo.Length} bytes");

                // Verificación: si el consolidado no generó una página por registro, hacer fallback a batch
                try
                {
                    using var verifyStream = new MemoryStream(reporteConsolidado.BytesArchivo);
                    using var verifyDoc = PdfReader.Open(verifyStream, PdfDocumentOpenMode.ReadOnly);
                    var consolidatedPages = verifyDoc.PageCount;
                    if (consolidatedPages < 2 || consolidatedPages != reportData.Count)
                    {
                        Console.WriteLine($"Advertencia: El PDF consolidado tiene {consolidatedPages} páginas para {reportData.Count} registros. Ejecutando fallback a generación batch.");
                        return await ExecuteBatchReportsCompressed(jsonString, reportType, userName);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"No se pudo verificar el número de páginas del consolidado: {ex.Message}. Continuando con split por páginas.");
                }

                // PASO 2: Dividir el PDF consolidado en archivos individuales
                var zipBytes = await SplitPdfIntoIndividualFiles(reporteConsolidado.BytesArchivo, reportData.Count, userName);

                // Crear el archivo de salida comprimido
                var nombreArchivoZip = $"Reportes_{reportType}_ConsolidatedSplit_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

                var archivoComprimido = new ArchivoResult
                {
                    NombreArchivo = nombreArchivoZip,
                    BytesArchivo = zipBytes,
                    Usuario = userName,
                    FechaGeneracion = DateTime.Now
                };

                Console.WriteLine($"Archivo ZIP con split generado exitosamente: {nombreArchivoZip}, Tamaño: {archivoComprimido.BytesArchivo.Length} bytes");

                return archivoComprimido;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ExecuteConsolidatedReportWithSplit: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Divide un PDF consolidado en archivos individuales por página y los comprime en un ZIP
        /// </summary>
        /// <param name="consolidatedPdfBytes">Bytes del PDF consolidado</param>
        /// <param name="recordCount">Número de registros (para validación)</param>
        /// <param name="userName">Nombre del usuario</param>
        /// <returns>Bytes del archivo ZIP con los PDFs individuales</returns>
        private async Task<byte[]> SplitPdfIntoIndividualFiles(byte[] consolidatedPdfBytes, int recordCount, string userName)
        {
            try
            {
                Console.WriteLine($"Iniciando split del PDF consolidado. Tamaño: {consolidatedPdfBytes.Length} bytes");

                using var zipStream = new MemoryStream();
                using (var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    // Crear un stream del PDF consolidado
                    using var pdfStream = new MemoryStream(consolidatedPdfBytes);
                    using var inputDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
                    int totalPages = inputDocument.PageCount;
                    Console.WriteLine($"PDF consolidado tiene {totalPages} páginas. Registros esperados: {recordCount}");

                    if (totalPages == 1 && recordCount > 1)
                    {
                        Console.WriteLine("Advertencia: El PDF consolidado tiene 1 sola página pero hay múltiples registros. Verifique su plantilla .trdp para que cada registro genere una página (por ejemplo, usando un List/Group con PageBreak).");
                    }

                    // Dividir cada página en un archivo individual
                    for (int pageNumber = 0; pageNumber < totalPages; pageNumber++)
                    {
                        try
                        {
                            Console.WriteLine($"Procesando página {pageNumber + 1} de {totalPages}");

                            // Crear un nuevo PDF con solo esta página
                            using var outputStream = new MemoryStream();
                            var outputDocument = new PdfDocument();

                            // Copiar la página específica al nuevo documento
                            var page = inputDocument.Pages[pageNumber];
                            outputDocument.AddPage(page);

                            // Guardar el documento de salida
                            outputDocument.Save(outputStream);
                            outputDocument.Close();

                            // Obtener los bytes del PDF individual
                            var individualPdfBytes = outputStream.ToArray();

                            if (individualPdfBytes.Length > 0)
                            {
                                // Crear nombre único para el archivo dentro del ZIP
                                var nombreArchivo = $"Reporte_Pagina_{(pageNumber + 1):D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                                // Agregar el archivo al ZIP
                                var zipEntry = zipArchive.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
                                using var entryStream = zipEntry.Open();
                                await entryStream.WriteAsync(individualPdfBytes, 0, individualPdfBytes.Length);

                                Console.WriteLine($"Página {pageNumber + 1} guardada como {nombreArchivo} ({individualPdfBytes.Length} bytes)");
                            }
                            else
                            {
                                Console.WriteLine($"Advertencia: La página {pageNumber + 1} resultó en un PDF vacío");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error procesando página {pageNumber + 1}: {ex.Message}");
                            // Continuar con la siguiente página en caso de error
                        }
                    }

                    Console.WriteLine($"Split completado. Total de páginas procesadas: {totalPages}");
                }

                var zipBytes = zipStream.ToArray();
                Console.WriteLine($"ZIP generado con tamaño: {zipBytes.Length} bytes");
                return zipBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en SplitPdfIntoIndividualFiles: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                // En caso de error, crear un ZIP con el PDF original como fallback
                Console.WriteLine("Creando fallback con PDF consolidado original...");
                using var fallbackZipStream = new MemoryStream();
                using (var zipArchive = new ZipArchive(fallbackZipStream, ZipArchiveMode.Create, true))
                {
                    var entryName = $"ReporteConsolidado_Fallback_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    var zipEntry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var entryStream = zipEntry.Open();
                    await entryStream.WriteAsync(consolidatedPdfBytes, 0, consolidatedPdfBytes.Length);
                }
                return fallbackZipStream.ToArray();
            }
        }

        // Método GuardarAsync eliminado de Report; use ArchivoResult.GuardarAsync

        /// <summary>
        /// Genera un reporte PDF consolidado (sin comprimir) a partir de un JSON con múltiples registros.
        /// </summary>
        /// <param name="jsonString">JSON que contiene una propiedad "Data" con los registros</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Usuario que genera el reporte</param>
        /// <returns>ArchivoResult con los bytes del PDF consolidado</returns>
        public ArchivoResult GenerateConsolidatedReport(string jsonString, string reportType, string userName = "SYSTEM")
        {
            Console.WriteLine($"Inicio de GenerateConsolidatedReport: {DateTime.Now}");

            if (string.IsNullOrWhiteSpace(jsonString))
            {
                throw new ArgumentException("El JSON del reporte no puede estar vacío", nameof(jsonString));
            }

            var reportData = ExtractDataFromJson(jsonString);
            if (reportData == null || !reportData.Any())
            {
                throw new InvalidOperationException("No se encontraron datos en el JSON del reporte");
            }

            Console.WriteLine($"Generando PDF consolidado con {reportData.Count} registros para el reporte {reportType}");
            var consolidado = GenerateTelerik(reportData, reportType, userName);
            if (consolidado?.BytesArchivo == null || consolidado.BytesArchivo.Length == 0)
            {
                throw new InvalidOperationException("No se pudo generar el PDF consolidado");
            }

            Console.WriteLine($"PDF consolidado generado. Tamaño: {consolidado.BytesArchivo.Length} bytes");
            return consolidado;
        }
    }

    /// <summary>
    /// Clase para representar el resultado del archivo generado
    /// </summary>
    public class ArchivoResult
    {
        public string NombreArchivo { get; set; } = string.Empty;
        public byte[]? BytesArchivo { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public DateTime FechaGeneracion { get; set; }

        /// <summary>
        /// Genera un nombre único para el archivo
        /// </summary>
        /// <param name="usuario">Usuario que genera el archivo</param>
        public void GenerarNombreUnico(string usuario)
        {
            var extension = Path.GetExtension(NombreArchivo);
            var nombreSinExtension = Path.GetFileNameWithoutExtension(NombreArchivo);
            var directorio = Path.GetDirectoryName(NombreArchivo) ?? "";

            var nombreUnico = $"{nombreSinExtension}_{usuario}_{Guid.NewGuid():N}{extension}";
            NombreArchivo = Path.Combine(directorio, nombreUnico);
        }

        /// <summary>
        /// Guarda el archivo en disco
        /// </summary>
        /// <returns>True si se guardó correctamente</returns>
        public async Task<bool> GuardarAsync()
        {
            try
            {
                if (BytesArchivo == null || BytesArchivo.Length == 0)
                {
                    return false;
                }

                var directorio = Path.GetDirectoryName(NombreArchivo);
                if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }

                await File.WriteAllBytesAsync(NombreArchivo, BytesArchivo);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando archivo: {ex.Message}");
                return false;
            }
        }
    }
}