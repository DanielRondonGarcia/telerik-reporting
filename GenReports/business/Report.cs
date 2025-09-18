using GenReports.Models;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;
using Telerik.Reporting;
using Telerik.Reporting.Processing;
using System.Collections.Concurrent;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf.Streaming;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.Navigation;
using Telerik.Windows.Documents.Fixed.Model.Actions;

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

                // Intentar obtener la propiedad "Data" sin sensibilidad a mayúsculas/minúsculas
                JsonElement dataElement;
                if (!TryGetPropertyCaseInsensitive(root, "Data", out dataElement))
                {
                    // Si no existe, aceptar también cuando la raíz es un arreglo
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        dataElement = root;
                    }
                    else
                    {
                        // Considerar el objeto completo como un solo registro
                        dataElement = root;
                    }
                }

                // Convertir la data a una lista de objetos
                var dataList = new List<object>();

                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        var itemObject = JsonSerializer.Deserialize<object>(item.GetRawText());
                        if (itemObject != null)
                        {
                            dataList.Add(itemObject);
                        }
                    }
                }
                else
                {
                    var singleObject = JsonSerializer.Deserialize<object>(dataElement.GetRawText());
                    if (singleObject != null)
                    {
                        dataList.Add(singleObject);
                    }
                }

                Console.WriteLine($"[JSON] Registros extraídos: {dataList.Count}");
                return dataList;
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Error parseando el JSON: {ex.Message}", nameof(reportJson));
            }
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
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
                var jsonDataSources = FindJsonDataSources(report);

                if (jsonDataSources.Count == 0)
                {
                    Console.WriteLine("No se encontraron JsonDataSource en el reporte. Se intentará agregar uno automáticamente.");

                    var dataSource = new Telerik.Reporting.JsonDataSource();
                    var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
                    var jsonArray = JsonSerializer.Serialize(reportData, jsonOptions);

                    dataSource.Source = jsonArray;
                    // Forzar DataSelector = "$" si la propiedad existe en esta versión
                    TrySetProperty(dataSource, "DataSelector", "$");

                    report.DataSource = dataSource;
                    Console.WriteLine($"Se agregó automáticamente un JsonDataSource al reporte (registros: {reportData.Count})");
                }
                else
                {
                    Console.WriteLine($"Se encontraron {jsonDataSources.Count} JsonDataSource en el reporte. Configurando...");

                    foreach (var dataSource in jsonDataSources)
                    {
                        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
                        var jsonArray = JsonSerializer.Serialize(reportData, jsonOptions);
                        dataSource.Source = jsonArray;
                        // Forzar DataSelector = "$" si la propiedad existe en esta versión
                        TrySetProperty(dataSource, "DataSelector", "$");
                    }
                    Console.WriteLine($"JsonDataSource configurado con {reportData.Count} registros");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configurando el origen de datos: {ex.Message}");
                throw;
            }
        }

        private static void TrySetProperty(object instance, string propertyName, object? value)
        {
            try
            {
                var prop = instance.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(instance, value);
                    Console.WriteLine($"[DataSource] Establecido {propertyName} = {value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSource] No fue posible establecer {propertyName}: {ex.Message}");
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

                var entries = new ConcurrentBag<ArchiveEntry>();
                // Generar un reporte individual para cada registro
                for (int i = 0; i < reportData.Count; i++)
                {
                    try
                    {
                        Console.WriteLine($"Generando reporte {i + 1} de {reportData.Count}");

                        var singleRecordData = new List<object> { reportData[i] };

                        // Generar el reporte individual
                        var reporteIndividual = GenerateTelerik(singleRecordData, reportType, userName);

                        if (reporteIndividual?.BytesArchivo != null)
                        {
                            // Crear nombre único para el archivo dentro del contenedor
                            var nombreArchivo = $"{reportType}_Registro_{i + 1:D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                            // Agregar a la lista de entradas para compresión
                            entries.Add(new ArchiveEntry(nombreArchivo, reporteIndividual.BytesArchivo));
                            Console.WriteLine($"Archivo {nombreArchivo} preparado para compresión");
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

                var compressor = new ArchiveCompressor();
                var build = await compressor.CreateArchivePrefer7zAsync(entries);

                // Crear el archivo de salida comprimido con nombre simple (sin ruta completa)
                var nombreArchivoZip = $"Reportes_{reportType}_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.{build.Extension}";

                var archivoComprimido = new ArchivoResult
                {
                    NombreArchivo = nombreArchivoZip,
                    BytesArchivo = build.Bytes,
                    Usuario = userName,
                    FechaGeneracion = DateTime.Now
                };

                Console.WriteLine($"Archivo comprimido generado exitosamente ({build.Extension}): {nombreArchivoZip}, Tamaño: {archivoComprimido.BytesArchivo.Length} bytes");

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
                    using var verifyDoc = PdfReader.Open(verifyStream, PdfDocumentOpenMode.Import);
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
                var build = await SplitPdfIntoIndividualFilesTelerik(reporteConsolidado.BytesArchivo, reportData.Count, userName);

                var nombreArchivoZip = $"Reportes_{reportType}_ConsolidatedSplit_{DateTime.Now:yyyyMMdd_HHmmss}.{build.Extension}";

                var archivoComprimido = new ArchivoResult
                {
                    NombreArchivo = nombreArchivoZip,
                    BytesArchivo = build.Bytes,
                    Usuario = userName,
                    FechaGeneracion = DateTime.Now
                };

                Console.WriteLine($"Archivo comprimido con split generado exitosamente: {nombreArchivoZip}, Tamaño: {archivoComprimido.BytesArchivo.Length} bytes");

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
        private async Task<ArchiveBuildResult> SplitPdfIntoIndividualFiles(byte[] consolidatedPdfBytes, int recordCount, string userName)
        {
            try
            {
                Console.WriteLine($"Iniciando split del PDF consolidado. Tamaño: {consolidatedPdfBytes.Length} bytes");

                // entries handled via entriesArr
                using var pdfStream = new MemoryStream(consolidatedPdfBytes);
                using var inputDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
                int totalPages = inputDocument.PageCount;
                Console.WriteLine($"PDF consolidado tiene {totalPages} páginas. Registros esperados: {recordCount}");
                var entriesArr = new ArchiveEntry[totalPages];
                var pageCopyLock = new object();

                // Dividir cada página en un archivo individual
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
                Parallel.For(0, totalPages, parallelOptions, pageNumber =>
                 {
                    try
                    {
                        Console.WriteLine($"Procesando página {pageNumber + 1} de {totalPages}");

                        // Crear un nuevo PDF con solo esta página
                        using var outputStream = new MemoryStream();
                        var outputDocument = new PdfDocument();

                        // Copiar la página específica al nuevo documento
                        lock (pageCopyLock)
                        {
                            var page = inputDocument.Pages[pageNumber];
                            outputDocument.AddPage(page);
                        }

                        // Guardar el documento de salida
                        outputDocument.Save(outputStream);
                        outputDocument.Close();

                        // Obtener los bytes del PDF individual
                        var individualPdfBytes = outputStream.ToArray();

                        if (individualPdfBytes.Length > 0)
                        {
                            // Crear nombre único para el archivo
                            var nombreArchivo = $"Reporte_Pagina_{(pageNumber + 1):D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                            // Agregar a la colección para compresión
                            entriesArr[pageNumber] = new ArchiveEntry(nombreArchivo, individualPdfBytes);

                            Console.WriteLine($"Página {pageNumber + 1} preparada como {nombreArchivo} ({individualPdfBytes.Length} bytes)");
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
                });

                // Asegurar orden estable por número de página después de paralelizar
                var entries = entriesArr.Where(e => e != null)!;
                
                var compressor = new ArchiveCompressor();
                var build = await compressor.CreateArchivePrefer7zAsync(entries);
                Console.WriteLine($"Archivo comprimido (split) generado en formato {build.Extension}. Tamaño: {build.Bytes.Length} bytes");
                return build;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en SplitPdfIntoIndividualFiles: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                // En caso de error, crear un archivo comprimido con el PDF original como fallback
                Console.WriteLine("Creando fallback con PDF consolidado original...");
                var compressor = new ArchiveCompressor();
                var build = await compressor.CreateArchivePrefer7zAsync(new[]
                {
                    new ArchiveEntry($"ReporteConsolidado_Fallback_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", consolidatedPdfBytes)
                });
                return build;
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

        /// <summary>
        /// Divide un PDF consolidado en archivos individuales por página utilizando Telerik PdfStreamWriter/PdfFileSource
        /// </summary>
        /// <param name="consolidatedPdfBytes">Bytes del PDF consolidado</param>
        /// <param name="recordCount">Número de registros (para validación)</param>
        /// <param name="userName">Nombre del usuario</param>
        /// <returns>Bytes del archivo ZIP con los PDFs individuales</returns>
        private async Task<ArchiveBuildResult> SplitPdfIntoIndividualFilesTelerik(byte[] consolidatedPdfBytes, int recordCount, string userName)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Console.WriteLine($"[Telerik] Iniciando split del PDF consolidado. Tamaño: {consolidatedPdfBytes.Length} bytes");

                using var inputStream = new MemoryStream(consolidatedPdfBytes);
                using var fileSource = new PdfFileSource(inputStream);

                int totalPages = fileSource.Pages.Length;
                Console.WriteLine($"[Telerik] PDF consolidado tiene {totalPages} páginas. Registros esperados: {recordCount}");

                var entries = new List<ArchiveEntry>();

                // 1) Intentar dividir por Bookmarks (Document Map)
                RadFixedDocument? fixedDoc = null;
                List<(string title, int pageIndex)> bookmarkStarts = new();
                try
                {
                    using var importStream = new MemoryStream(consolidatedPdfBytes);
                    var provider = new PdfFormatProvider();
                    fixedDoc = provider.Import(importStream);
                    bookmarkStarts = GetBookmarkStartPages(fixedDoc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Telerik] No fue posible leer bookmarks: {ex.Message}. Se intentará fallback por página.");
                }

                if (bookmarkStarts.Count > 0 && fixedDoc != null)
                {
                    Console.WriteLine($"[Telerik] Se encontraron {bookmarkStarts.Count} bookmarks. Dividiendo por rangos de páginas (RadFixedDocument)...");

                    for (int i = 0; i < bookmarkStarts.Count; i++)
                    {
                        var start = bookmarkStarts[i].pageIndex;
                        var end = (i < bookmarkStarts.Count - 1) ? bookmarkStarts[i + 1].pageIndex - 1 : totalPages - 1;

                        if (start < 0 || start >= totalPages || end < start)
                        {
                            Console.WriteLine($"[Telerik] Rango inválido derivado de bookmarks: {start}-{end}. Se omite.");
                            continue;
                        }

                        using var ms = new MemoryStream();
                        // Escribir usando RadFixedDocument (mismas páginas que resolvieron los bookmarks)
                        using (var writer = new PdfStreamWriter(ms, leaveStreamOpen: true))
                        {
                            for (int p = start; p <= end; p++)
                            {
                                var srcPage = fixedDoc.Pages[p];
                                using var pageWriter = writer.BeginPage(srcPage.Size, srcPage.Rotation);
                                pageWriter.WriteContent(srcPage);
                                Console.WriteLine($"[Telerik]  - Escribiendo página {p + 1} (RadFixed)");
                            }
                        }

                        var individualPdfBytes = ms.ToArray();

                        // Fallback de robustez: si el tamaño es muy pequeño (< 1KB), reintentar con PdfFileSource.WritePage
                        if (individualPdfBytes.Length < 1024)
                        {
                            Console.WriteLine($"[Telerik]  - Documento pequeño ({individualPdfBytes.Length} bytes). Reintentando con PdfFileSource.WritePage para {start + 1}-{end + 1}...");
                            ms.SetLength(0);
                            using (var writer = new PdfStreamWriter(ms, leaveStreamOpen: true))
                            {
                                for (int p = start; p <= end; p++)
                                {
                                    var pageSource = fileSource.Pages[p];
                                    writer.WritePage(pageSource);
                                    Console.WriteLine($"[Telerik]  - Escribiendo página {p + 1} (FileSource)");
                                }
                            }
                            individualPdfBytes = ms.ToArray();
                        }

                        if (individualPdfBytes.Length == 0)
                        {
                            Console.WriteLine($"[Telerik] Advertencia: El rango {start + 1}-{end + 1} resultó en un PDF vacío");
                            continue;
                        }

                        var baseName = string.IsNullOrWhiteSpace(bookmarkStarts[i].title) ? $"Registro_{i + 1:D4}" : SanitizeFileName(bookmarkStarts[i].title);
                        var nombreArchivo = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                        entries.Add(new ArchiveEntry(nombreArchivo, individualPdfBytes));
                        Console.WriteLine($"[Telerik] Rango {start + 1}-{end + 1} preparado como {nombreArchivo} ({individualPdfBytes.Length} bytes)");
                    }
                }

                // 2) Fallback: si no hay entries por bookmarks, usar split hoja por hoja (comportamiento anterior)
                if (entries.Count == 0)
                {
                    Console.WriteLine("[Telerik] No se generaron entradas por bookmarks. Aplicando split por página...");

                    for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
                    {
                        try
                        {
                            using var ms = new MemoryStream();
                            using (var writer = new PdfStreamWriter(ms, leaveStreamOpen: true))
                            {
                                var pageSource = fileSource.Pages[pageIndex];
                                writer.WritePage(pageSource);
                            }

                            var individualPdfBytes = ms.ToArray();
                            if (individualPdfBytes.Length == 0)
                            {
                                Console.WriteLine($"[Telerik] Advertencia: La página {pageIndex + 1} resultó en un PDF vacío");
                                continue;
                            }

                            var nombreArchivo = $"Reporte_Pagina_{(pageIndex + 1):D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                            entries.Add(new ArchiveEntry(nombreArchivo, individualPdfBytes));
                            Console.WriteLine($"[Telerik] Página {pageIndex + 1} preparada como {nombreArchivo} ({individualPdfBytes.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Telerik] Error procesando página {pageIndex + 1}: {ex.Message}");
                        }
                    }
                }

                var compressor = new ArchiveCompressor();
                var build = await compressor.CreateArchivePrefer7zAsync(entries);
                sw.Stop();
                Console.WriteLine($"[Telerik] Split + compresión completados en {sw.Elapsed}. Formato: {build.Extension}. Tamaño: {build.Bytes.Length} bytes");
                return build;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"[Telerik] Error en SplitPdfIntoIndividualFilesTelerik tras {sw.Elapsed}: {ex.Message}");
                Console.WriteLine($"[Telerik] StackTrace: {ex.StackTrace}");

                // Fallback: devolver ZIP con el PDF consolidado
                var compressor = new ArchiveCompressor();
                var build = await compressor.CreateArchivePrefer7zAsync(new[]
                {
                    new ArchiveEntry($"ReporteConsolidado_Fallback_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", consolidatedPdfBytes)
                });
                return build;
            }
        }

        // Extrae las páginas de inicio de cada bookmark (Document Map) y las ordena
        private static List<(string title, int pageIndex)> GetBookmarkStartPages(RadFixedDocument document)
        {
            var list = new List<(string title, int pageIndex)>();
            if (document == null) return list;

            int? ResolvePageIndex(BookmarkItem b)
            {
                try
                {
                    // Preferir Destination directo
                    var dest = b.Destination;
                    if (dest?.Page != null)
                    {
                        int idx = document.Pages.IndexOf(dest.Page);
                        if (idx >= 0) return idx;
                    }

                    // Luego NamedDestination -> Destination
                    var nd = b.NamedDestination;
                    if (nd?.Destination?.Page != null)
                    {
                        int idx = document.Pages.IndexOf(nd.Destination.Page);
                        if (idx >= 0) return idx;
                    }

                    // Nota: en esta versión de Telerik no existe BookmarkItem.Actions
                    // Se mantiene compatibilidad usando la propiedad obsoleta 'Action' cuando aplique.
#pragma warning disable CS0618
                    if (b.Action is GoToAction go2)
                    {
                        if (go2.Destination?.Page != null)
                        {
                            int idx = document.Pages.IndexOf(go2.Destination.Page);
                            if (idx >= 0) return idx;
                        }
                        if (go2.NamedDestination?.Destination?.Page != null)
                        {
                            int idx = document.Pages.IndexOf(go2.NamedDestination.Destination.Page);
                            if (idx >= 0) return idx;
                        }
                    }
#pragma warning restore CS0618
                }
                catch { }
                return null;
            }

            void Visit(IEnumerable<BookmarkItem> items)
            {
                foreach (var b in items)
                {
                    try
                    {
                        var idx = ResolvePageIndex(b);
                        Console.WriteLine($"[Telerik] Bookmark: '{b.Title}' => pageIndex: {(idx.HasValue ? idx.Value.ToString() : "null")}");
                        if (idx.HasValue)
                        {
                            list.Add((b.Title, idx.Value));
                        }
                    }
                    catch { /* ignorar bookmark inválido */ }

                    if (b.Children != null && b.Children.Count > 0)
                    {
                        Visit(b.Children);
                    }
                }
            }

            Visit(document.Bookmarks);

            // Ordenar y deduplicar por pageIndex
            var result = list
                .GroupBy(x => x.pageIndex)
                .Select(g => g.First())
                .OrderBy(x => x.pageIndex)
                .ToList();

            Console.WriteLine($"[Telerik] Bookmarks válidos encontrados: {result.Count}");
            for (int i = 0; i < result.Count; i++)
            {
                Console.WriteLine($"[Telerik]  #{i + 1}: page={result[i].pageIndex + 1}, title='{result[i].title}'");
            }

            return result;
        }

        // Sanea un texto para usarlo como nombre de archivo
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Archivo";
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            // Evitar nombres extremadamente largos
            if (sanitized.Length > 120) sanitized = sanitized.Substring(0, 120);
            return string.IsNullOrWhiteSpace(sanitized) ? "Archivo" : sanitized;
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