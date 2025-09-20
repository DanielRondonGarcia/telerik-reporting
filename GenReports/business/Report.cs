using GenReports.Models;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telerik.Reporting;
using Telerik.Reporting.Processing;
using System.Collections.Concurrent;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf.Streaming;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.Navigation;
using Telerik.Windows.Documents.Fixed.Model.Actions;
using System.Linq;

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
                "USUARIO" => "GET_USERS_DATA.trdp",
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
        /// Genera el reporte usando Telerik Reporting con timeout
        /// </summary>
        /// <param name="reportData">Datos del reporte</param>
        /// <param name="reportType">Tipo de reporte</param>
        /// <param name="userName">Nombre del usuario</param>
        /// <param name="timeout">Tiempo máximo de espera</param>
        /// <returns>Archivo generado</returns>
        private ArchivoResult GenerateTelerikWithTimeout(List<object> reportData, string reportType, string userName, TimeSpan timeout)
        {
            var cancellationTokenSource = new CancellationTokenSource(timeout);
            
            try
            {
                var task = Task.Run(() => GenerateTelerik(reportData, reportType, userName), cancellationTokenSource.Token);
                return task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"La generación del reporte excedió el tiempo límite de {timeout.TotalMinutes} minutos");
            }
            finally
            {
                cancellationTokenSource?.Dispose();
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
                if (telerikReportProcessor == null)
                {
                    throw new InvalidOperationException("No se pudo crear el procesador de reportes de Telerik");
                }

                // Configurar información del dispositivo
                var deviceInfo = new System.Collections.Hashtable();

                // Cargar la definición del reporte desde el archivo .trdp
                var reportPackager = new ReportPackager();
                if (reportPackager == null)
                {
                    throw new InvalidOperationException("No se pudo crear el empaquetador de reportes");
                }

                Telerik.Reporting.Report reportDefinition;

                // Simplificado: cargar directamente y permitir que la excepción se propague al catch externo si algo falla
                using (var fs = new FileStream(plantillaPath, FileMode.Open, FileAccess.Read))
                {
                    var document = reportPackager.UnpackageDocument(fs);
                    reportDefinition = document as Telerik.Reporting.Report
                        ?? throw new InvalidOperationException($"El archivo {plantillaPath} no contiene un reporte válido de Telerik");
                }
                Console.WriteLine($"Reporte cargado exitosamente desde: {plantillaPath}");

                // Validar que reportDefinition no sea null
                if (reportDefinition == null)
                {
                    throw new InvalidOperationException("La definición del reporte es nula después de la carga");
                }

                // Validar que reportData no sea null o vacío
                if (reportData == null || !reportData.Any())
                {
                    throw new InvalidOperationException("Los datos del reporte son nulos o están vacíos");
                }

                // Configurar el origen de datos
                ConfigureDataSource(reportDefinition, reportData);

                // Crear el InstanceReportSource con validaciones adicionales
                var instanceReportSource = new InstanceReportSource();
                if (instanceReportSource == null)
                {
                    throw new InvalidOperationException("No se pudo crear la instancia del reporte");
                }

                instanceReportSource.ReportDocument = reportDefinition;

                // Validar que ReportDocument se asignó correctamente
                if (instanceReportSource.ReportDocument == null)
                {
                    throw new InvalidOperationException("No se pudo asignar el documento del reporte a la instancia");
                }

                // Agregar parámetros al reporte
                AddReportParameters(instanceReportSource, reportType, userName);

                // Renderizar el reporte
                var formatoSalida = "PDF";
                RenderingResult resultado;

                try
                {
                    Console.WriteLine("Iniciando procesamiento del reporte...");
                    
                    // Validaciones finales antes del renderizado
                    if (string.IsNullOrEmpty(formatoSalida))
                    {
                        throw new InvalidOperationException("El formato de salida no puede ser nulo o vacío");
                    }

                    if (instanceReportSource == null)
                    {
                        throw new InvalidOperationException("La instancia del reporte es nula antes del renderizado");
                    }

                    if (deviceInfo == null)
                    {
                        throw new InvalidOperationException("La información del dispositivo es nula");
                    }

                    resultado = telerikReportProcessor.RenderReport(formatoSalida, instanceReportSource, deviceInfo);
                    
                    // Validar que el resultado no sea null
                    if (resultado == null)
                    {
                        throw new InvalidOperationException("El resultado del renderizado es nulo");
                    }

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
                // Validaciones iniciales
                if (report == null)
                {
                    throw new ArgumentNullException(nameof(report), "El reporte no puede ser nulo");
                }

                if (reportData == null)
                {
                    throw new ArgumentNullException(nameof(reportData), "Los datos del reporte no pueden ser nulos");
                }

                if (!reportData.Any())
                {
                    throw new ArgumentException("Los datos del reporte no pueden estar vacíos", nameof(reportData));
                }

                Console.WriteLine($"Configurando origen de datos para {reportData.Count} registros");

                var jsonDataSources = FindJsonDataSources(report);

                if (jsonDataSources.Count == 0)
                {
                    // No hay JsonDataSource definidos en la plantilla: usar ObjectDataSource para evitar re-serializar el JSON
                    var objectDataSource = new Telerik.Reporting.ObjectDataSource
                    {
                        DataSource = reportData
                    };

                    if (objectDataSource == null)
                    {
                        throw new InvalidOperationException("No se pudo crear el ObjectDataSource");
                    }

                    report.DataSource = objectDataSource;
                    Console.WriteLine($"ObjectDataSource configurado con {reportData.Count} registros (sin re-serialización)");
                }
                else
                {
                    Console.WriteLine($"Se encontraron {jsonDataSources.Count} JsonDataSource en el reporte. Configurando...");

                    // Serializar una sola vez y reutilizar según DataSelector existente
                    var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
                    var jsonArray = JsonSerializer.Serialize(reportData, jsonOptions);               // Ej: [ {..}, {..} ]
                    var jsonWrapped = JsonSerializer.Serialize(new { Data = reportData }, jsonOptions); // Ej: { "Data": [ {..}, {..} ] }

                    foreach (var dataSource in jsonDataSources)
                    {
                        // Leer el DataSelector actual (si existe) para no romper plantillas que esperan $.Data u otro path
                        var selector = TryGetStringProperty(dataSource, "DataSelector");
                        var needsWrapper = !string.IsNullOrWhiteSpace(selector) && selector.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0;

                        // Si la plantilla hace referencia a $.Data, debemos envolver el arreglo en un objeto con la propiedad Data
                        if (needsWrapper)
                        {
                            TrySetProperty(dataSource, "Source", jsonWrapped);
                            Console.WriteLine("JsonDataSource.Source configurado con objeto envuelto { Data = [...] } por DataSelector='" + selector + "'");
                        }
                        else
                        {
                            // Caso general: la plantilla apunta a la raíz ($) -> enviar arreglo directo
                            TrySetProperty(dataSource, "Source", jsonArray);
                            // Solo establecer DataSelector cuando esté vacío o nulo, evitando sobreescribir configuraciones de la plantilla
                            if (string.IsNullOrWhiteSpace(selector))
                            {
                                TrySetProperty(dataSource, "DataSelector", "$");
                            }
                        }
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
                    // Evitar volcar JSONs completos en el log: recortar a 100 caracteres
                    string displayValue;
                    if (value is string s)
                    {
                        displayValue = s.Length <= 100 ? s : s.Substring(0, 100) + "...";
                    }
                    else
                    {
                        var text = value?.ToString() ?? "null";
                        displayValue = text.Length <= 100 ? text : text.Substring(0, 100) + "...";
                    }
                    Console.WriteLine($"[DataSource] Establecido {propertyName} = {displayValue}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSource] No fue posible establecer {propertyName}: {ex.Message}");
            }
        }

        private static string? TryGetStringProperty(object instance, string propertyName)
        {
            try
            {
                var prop = instance.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanRead)
                {
                    var val = prop.GetValue(instance);
                    return val?.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataSource] No fue posible leer {propertyName}: {ex.Message}");
            }
            return null;
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
            // Validaciones de entrada
            if (instanceReportSource == null)
            {
                throw new ArgumentNullException(nameof(instanceReportSource), "La instancia del reporte no puede ser nula");
            }

            if (instanceReportSource.Parameters == null)
            {
                throw new InvalidOperationException("La colección de parámetros del reporte es nula");
            }

            if (string.IsNullOrWhiteSpace(reportType))
            {
                reportType = "DESCONOCIDO";
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                userName = "SYSTEM";
            }

            try
            {
                instanceReportSource.Parameters.Add("nombreReporte", reportType);
                instanceReportSource.Parameters.Add("versionReporte", "v1.0");
                instanceReportSource.Parameters.Add("userSistema", userName);
                instanceReportSource.Parameters.Add("userNombre", userName);
                instanceReportSource.Parameters.Add("tituloReporte", $"Reporte {reportType}");
                instanceReportSource.Parameters.Add("fechaGeneracion", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                
                Console.WriteLine($"Parámetros agregados exitosamente al reporte: {reportType}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error agregando parámetros al reporte: {ex.Message}");
                throw new InvalidOperationException($"Error configurando parámetros del reporte: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Ejecuta reportes masivos generando un archivo individual por cada registro y los comprime en un ZIP
        /// </summary>
        /// <param name="reportJson">JSON que contiene la información del reporte con la estructura: { "Data": [...] }</param>
        /// <param name="reportType">Tipo de reporte a generar</param>
        /// <param name="userName">Nombre del usuario que genera el reporte</param>
        /// <returns>Archivo ZIP comprimido con todos los reportes individuales</returns>
        public async Task<ArchivoResult> ExecuteBatchReportsCompressed(string reportJson, string reportType, string userName = "SYSTEM", Action<int, int, string>? onProgress = null)
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
                // Generar un reporte individual para cada registro con manejo mejorado de errores
                var erroresEncontrados = new List<string>();
                var reportesExitosos = 0;

                for (int i = 0; i < reportData.Count; i++)
                {
                    var maxReintentos = 3;
                    var reintentoActual = 0;
                    var reporteGenerado = false;

                    while (reintentoActual < maxReintentos && !reporteGenerado)
                    {
                        try
                        {
                            Console.WriteLine($"Generando reporte {i + 1} de {reportData.Count} (intento {reintentoActual + 1})");

                            // Validar que el registro no sea null
                            if (reportData[i] == null)
                            {
                                throw new InvalidOperationException($"El registro {i + 1} es nulo");
                            }

                            var singleRecordData = new List<object> { reportData[i] };

                            // Generar el reporte individual con timeout
                            var reporteIndividual = GenerateTelerikWithTimeout(singleRecordData, reportType, userName, TimeSpan.FromMinutes(5));

                            if (reporteIndividual?.BytesArchivo != null && reporteIndividual.BytesArchivo.Length > 0)
                            {
                                // Crear nombre único para el archivo dentro del contenedor
                                var nombreArchivo = $"{reportType}_Registro_{i + 1:D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                                // Agregar a la lista de entradas para compresión
                                entries.Add(new ArchiveEntry(nombreArchivo, reporteIndividual.BytesArchivo));
                                Console.WriteLine($"Archivo {nombreArchivo} preparado para compresión (tamaño: {reporteIndividual.BytesArchivo.Length} bytes)");
                                reporteGenerado = true;
                                reportesExitosos++;
                            }
                            else
                            {
                                throw new InvalidOperationException($"El reporte generado para el registro {i + 1} está vacío o es nulo");
                            }
                        }
                        catch (Exception ex)
                        {
                            reintentoActual++;
                            var mensajeError = $"Error generando reporte individual {i + 1} (intento {reintentoActual}): {ex.Message}";
                            Console.WriteLine(mensajeError);

                            if (reintentoActual >= maxReintentos)
                            {
                                erroresEncontrados.Add($"Registro {i + 1}: {ex.Message}");
                                Console.WriteLine($"Se agotaron los reintentos para el registro {i + 1}. Continuando con el siguiente.");
                            }
                            else
                            {
                                // Esperar un poco antes del siguiente reintento
                                await Task.Delay(1000 * reintentoActual);
                            }
                        }
                    }
                }

                // Nota: se asume que existen variables locales como reportData, erroresEncontrados, entries, etc.
                // Iniciar progreso si es posible
                try
                {
                    // Asegurar conteo total antes del bucle si ya está disponible
                    // (reportData es definido más abajo en el método original)
                }
                catch { }

                for (int i = 0; i < reportData.Count; i++)
                {
                    // Dentro del bucle por registro
                    // Al finalizar el procesamiento de cada registro (éxito o error), reportar progreso
                    try
                    {
                        onProgress?.Invoke(Math.Min(i + 1, reportData.Count), reportData.Count, "batch");
                    }
                    catch { }
                }

                Console.WriteLine($"Procesamiento completado: {reportesExitosos} reportes exitosos de {reportData.Count} registros");

                if (erroresEncontrados.Any())
                {
                    Console.WriteLine($"Se encontraron {erroresEncontrados.Count} errores:");
                    foreach (var error in erroresEncontrados)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                }

                if (reportesExitosos == 0)
                {
                    throw new InvalidOperationException($"No se pudo generar ningún reporte. Errores: {string.Join("; ", erroresEncontrados)}");
                }

                var build = await ArchiveCompressor.CreateArchiveAsync(entries);

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
        public async Task<ArchivoResult> ExecuteConsolidatedReportWithSplit(string jsonString, string reportType, string userName = "SYSTEM", Action<int, int, string>? onProgress = null)
        {
            try
            {
                Console.WriteLine($"Inicio de ExecuteConsolidatedReportWithSplit: {DateTime.Now}");

                // Parsear el JSON para extraer la data
                var reportData = ExtractDataFromJson(jsonString);
                var total = reportData.Count;

                // Lógica de batching configurable para evitar sobrecarga de memoria / índices fuera de rango
                var batchSize = (_reportsConfig?.ConsolidatedSplitBatchSize ?? 0) > 0
                    ? _reportsConfig.ConsolidatedSplitBatchSize
                    : int.MaxValue;

                if (total > batchSize)
                {
                    Console.WriteLine($"Procesamiento en lotes activado: batchSize={batchSize}, totalRegistros={total}");
                    var aggregateEntries = new List<ArchiveEntry>();

                    int processed = 0;
                    int batchIndex = 0;
                    while (processed < total)
                    {
                        var size = Math.Min(batchSize, total - processed);
                        var batchData = reportData.Skip(processed).Take(size).ToList();

                        Console.WriteLine($"[Batch] Generando consolidado para lote {batchIndex + 1} de tamaño {size} (offset {processed})");
                        var reporteConsolidado = GenerateTelerik(batchData, reportType, userName);
                        Console.WriteLine($"[Batch] PDF consolidado (lote {batchIndex + 1}) tamaño: {reporteConsolidado.BytesArchivo.Length} bytes");

                        var prefix = $"B{(batchIndex + 1):D3}_";
                        // Llenar el acumulador de entradas SIN comprimir por lote
                        await SplitPdfIntoIndividualFilesTelerik(
                            reporteConsolidado.BytesArchivo,
                            size,
                            userName,
                            aggregateEntries,
                            prefix,
                            returnCompressed: false
                        );

                        processed += size;
                        batchIndex++;
                        try { onProgress?.Invoke(processed, total, "split-batch"); } catch { }
                    }

                    // Comprimir una sola vez con todas las entradas acumuladas
                    var buildAgg = await ArchiveCompressor.CreateArchiveAsync(aggregateEntries);
                    
                    var nombreZipAgg = $"Reportes_{reportType}_ConsolidatedSplit_{DateTime.Now:yyyyMMdd_HHmmss}.{buildAgg.Extension}";

                    var archivoAgg = new ArchivoResult
                    {
                        NombreArchivo = nombreZipAgg,
                        BytesArchivo = buildAgg.Bytes,
                        Usuario = userName,
                        FechaGeneracion = DateTime.Now
                    };

                    Console.WriteLine($"Archivo comprimido (batched) con split generado exitosamente: {nombreZipAgg}, Tamaño: {archivoAgg.BytesArchivo.Length} bytes");
                    return archivoAgg;
                }
                else
                {
                    Console.WriteLine($"Generando reporte consolidado con {total} registros");

                    // PASO 1: Generar un PDF consolidado reutilizando GenerateConsolidatedReport
                    var reporteConsolidado = GenerateTelerik(reportData, reportType, userName);
                    Console.WriteLine($"PDF consolidado generado exitosamente. Tamaño: {reporteConsolidado.BytesArchivo.Length} bytes");

                    // Verificar el número de páginas del PDF consolidado para información
                    try
                    {
                        using var verifyStream = new MemoryStream(reporteConsolidado.BytesArchivo);
                        using var fileSourceVerify = new PdfFileSource(verifyStream);
                        var consolidatedPages = fileSourceVerify.Pages.Length;
                        Console.WriteLine($"PDF consolidado contiene {consolidatedPages} páginas para {total} registros.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"No se pudo verificar el número de páginas del consolidado: {ex.Message}. Continuando con split por páginas.");
                    }

                    // Si supera el umbral, usar escritura a disco y compresión desde directorio
                    var threshold = (_reportsConfig?.ConsolidatedSplitDiskThreshold ?? 10000);
                    var useDisk = total > threshold;
                    if (useDisk)
                    {
                        var tempRoot = Path.Combine(Path.GetTempPath(), "GenReports_Split");
                        Directory.CreateDirectory(tempRoot);
                        var jobDir = Path.Combine(tempRoot, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
                        Directory.CreateDirectory(jobDir);

                        await SplitPdfToDirectoryTelerik(reporteConsolidado.BytesArchivo, total, jobDir, userName);

                        var buildDisk = await ArchiveCompressor.CreateArchiveFromDirectoryAsync(jobDir);
                        
                        var nombreArchivoZipDisk = $"Reportes_{reportType}_ConsolidatedSplit_{DateTime.Now:yyyyMMdd_HHmmss}.{buildDisk.Extension}";

                        var archivoComprimidoDisk = new ArchivoResult
                        {
                            NombreArchivo = nombreArchivoZipDisk,
                            BytesArchivo = buildDisk.Bytes,
                            Usuario = userName,
                            FechaGeneracion = DateTime.Now
                        };

                        try { onProgress?.Invoke(total, total, "split-disk"); } catch { }
                        try { Directory.Delete(jobDir, true); } catch (Exception cleanupEx) { Console.WriteLine($"[Disk] No se pudo eliminar el directorio temporal '{jobDir}': {cleanupEx.Message}"); }

                        Console.WriteLine($"Archivo comprimido (disk) con split generado exitosamente: {nombreArchivoZipDisk}, Tamaño: {archivoComprimidoDisk.BytesArchivo.Length} bytes");
                        return archivoComprimidoDisk;
                    }

                    // PASO 2: Dividir el PDF consolidado en archivos individuales y comprimir (memoria)
                    var build = await SplitPdfIntoIndividualFilesTelerik(reporteConsolidado.BytesArchivo, total, userName);

                    var nombreArchivoZip = $"Reportes_{reportType}_ConsolidatedSplit_{DateTime.Now:yyyyMMdd_HHmmss}.{build.Extension}";

                    var archivoComprimido = new ArchivoResult
                    {
                        NombreArchivo = nombreArchivoZip,
                        BytesArchivo = build.Bytes,
                        Usuario = userName,
                        FechaGeneracion = DateTime.Now
                    };

                    try { onProgress?.Invoke(total, total, "split"); } catch { }

                    Console.WriteLine($"Archivo comprimido con split generado exitosamente: {nombreArchivoZip}, Tamaño: {archivoComprimido.BytesArchivo.Length} bytes");
                    return archivoComprimido;
                }
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
        private async Task<ArchiveBuildResult> SplitPdfIntoIndividualFiles(
            byte[] consolidatedPdfBytes,
            int recordCount,
            string userName,
            List<ArchiveEntry>? aggregateEntries = null,
            string? fileNamePrefix = null,
            bool returnCompressed = true)
        {
            throw new NotSupportedException("Este método ha sido reemplazado por SplitPdfIntoIndividualFilesTelerik y ya no está disponible.");
        }

        /// <summary>
        /// Divide un PDF consolidado en archivos individuales por página utilizando Telerik PdfStreamWriter/PdfFileSource
        /// </summary>
        /// <param name="consolidatedPdfBytes">Bytes del PDF consolidado</param>
        /// <param name="recordCount">Número de registros (para validación)</param>
        /// <param name="userName">Nombre del usuario</param>
        /// <param name="aggregateEntries">Acumulador opcional de entradas; si se proporciona, las entradas se agregan aquí</param>
        /// <param name="fileNamePrefix">Prefijo opcional para nombres de archivo (p.ej., por lote)</param>
        /// <param name="returnCompressed">Si es true, devuelve un archivo comprimido; si es false, solo llena aggregateEntries</param>
        /// <returns>Resultado de compresión o vacío si returnCompressed=false</returns>
        private async Task<ArchiveBuildResult> SplitPdfIntoIndividualFilesTelerik(
            byte[] consolidatedPdfBytes,
            int recordCount,
            string userName,
            List<ArchiveEntry>? aggregateEntries = null,
            string? fileNamePrefix = null,
            bool returnCompressed = true)
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
                var prefix = string.IsNullOrEmpty(fileNamePrefix) ? string.Empty : fileNamePrefix;

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
                        var nombreArchivo = $"{prefix}{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
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

                            var nombreArchivo = $"{prefix}Reporte_Pagina_{(pageIndex + 1):D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                            entries.Add(new ArchiveEntry(nombreArchivo, individualPdfBytes));
                            Console.WriteLine($"[Telerik] Página {pageIndex + 1} preparada como {nombreArchivo} ({individualPdfBytes.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Telerik] Error procesando página {pageIndex + 1}: {ex.Message}");
                        }
                    }
                }

                // Si se pasa un acumulador y no se requiere devolver comprimido, agregar entradas y salir
                if (aggregateEntries != null)
                {
                    aggregateEntries.AddRange(entries);
                }
                if (!returnCompressed)
                {
                    sw.Stop();
                    Console.WriteLine($"[Telerik] Split completado. {entries.Count} entradas agregadas al acumulador. Sin compresión en este paso.");
                    return new ArchiveBuildResult { Bytes = Array.Empty<byte>(), Extension = "zip", UsedSevenZip = false };
                }

                var build = await ArchiveCompressor.CreateArchiveAsync(entries);
                sw.Stop();
                Console.WriteLine($"[Telerik] Split + compresión completados en {sw.Elapsed}. Formato: {build.Extension}. Tamaño: {build.Bytes.Length} bytes");
                return build;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"[Telerik] Error en SplitPdfIntoIndividualFilesTelerik tras {sw.Elapsed}: {ex.Message}");
                Console.WriteLine($"[Telerik] StackTrace: {ex.StackTrace}");

                // Fallback: si estamos en modo acumulador sin compresión, agregar el PDF consolidado al acumulador
                if (aggregateEntries != null && !returnCompressed)
                {
                    aggregateEntries.Add(new ArchiveEntry($"ReporteConsolidado_Fallback_{DateTime.Now:yyyyMMdd_HHmmss}.pdf", consolidatedPdfBytes));
                    return new ArchiveBuildResult { Bytes = Array.Empty<byte>(), Extension = "zip", UsedSevenZip = false };
                }

                // Fallback: devolver ZIP con el PDF consolidado
                var build = await ArchiveCompressor.CreateArchiveAsync(new[]
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

        private async Task SplitPdfToDirectoryTelerik(byte[] consolidatedPdfBytes, int recordCount, string outputDir, string userName)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                Console.WriteLine($"[Telerik-Disk] Iniciando split directo a disco. Bytes={consolidatedPdfBytes.Length}, Registros={recordCount}, Dir='{outputDir}'");

                using var inputStream = new MemoryStream(consolidatedPdfBytes);
                using var fileSource = new PdfFileSource(inputStream);

                int totalPages = fileSource.Pages.Length;

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
                    Console.WriteLine($"[Telerik-Disk] No fue posible leer bookmarks: {ex.Message}. Continuando con fallback por página si aplica.");
                }

                int generated = 0;
                string prefix = string.Empty;

                if (bookmarkStarts.Count > 0 && fixedDoc != null)
                {
                    Console.WriteLine($"[Telerik-Disk] {bookmarkStarts.Count} bookmarks detectados. Dividiendo por rangos...");

                    for (int i = 0; i < bookmarkStarts.Count; i++)
                    {
                        var start = bookmarkStarts[i].pageIndex;
                        var end = (i < bookmarkStarts.Count - 1) ? bookmarkStarts[i + 1].pageIndex - 1 : totalPages - 1;

                        if (start < 0 || start >= totalPages || end < start)
                        {
                            Console.WriteLine($"[Telerik-Disk] Rango inválido {start}-{end}. Se omite.");
                            continue;
                        }

                        var baseName = string.IsNullOrWhiteSpace(bookmarkStarts[i].title) ? $"Registro_{i + 1:D4}" : SanitizeFileName(bookmarkStarts[i].title);
                        var fileName = Path.Combine(outputDir, $"{prefix}{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                        // Primero construir en memoria para validar tamaño y luego escribir a disco
                        using var ms = new MemoryStream();
                        using (var writer = new PdfStreamWriter(ms, leaveStreamOpen: true))
                        {
                            for (int p = start; p <= end; p++)
                            {
                                var srcPage = fixedDoc.Pages[p];
                                using var pageWriter = writer.BeginPage(srcPage.Size, srcPage.Rotation);
                                pageWriter.WriteContent(srcPage);
                            }
                        }

                        var bytes = ms.ToArray();

                        if (bytes.Length < 1024)
                        {
                            ms.SetLength(0);
                            using (var writer = new PdfStreamWriter(ms, leaveStreamOpen: true))
                            {
                                for (int p = start; p <= end; p++)
                                {
                                    var pageSource = fileSource.Pages[p];
                                    writer.WritePage(pageSource);
                                }
                            }
                            bytes = ms.ToArray();
                        }

                        if (bytes.Length == 0)
                        {
                            Console.WriteLine($"[Telerik-Disk] Advertencia: El rango {start + 1}-{end + 1} resultó vacío.");
                            continue;
                        }

                        await File.WriteAllBytesAsync(fileName, bytes);
                        generated++;
                        if (generated % 100 == 0) { try { Console.WriteLine($"[Telerik-Disk] Generados {generated} archivos..."); } catch { } }
                    }
                }

                if (generated == 0)
                {
                    Console.WriteLine("[Telerik-Disk] No se generaron archivos por bookmarks. Aplicando split por página...");
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

                            var bytes = ms.ToArray();
                            if (bytes.Length == 0)
                            {
                                Console.WriteLine($"[Telerik-Disk] Advertencia: La página {pageIndex + 1} resultó vacía.");
                                continue;
                            }

                            var fileName = Path.Combine(outputDir, $"{prefix}Reporte_Pagina_{(pageIndex + 1):D4}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                            await File.WriteAllBytesAsync(fileName, bytes);
                            generated++;
                            if (generated % 100 == 0) { try { Console.WriteLine($"[Telerik-Disk] Generados {generated} archivos..."); } catch { } }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Telerik-Disk] Error procesando página {pageIndex + 1}: {ex.Message}");
                        }
                    }
                }

                sw.Stop();
                Console.WriteLine($"[Telerik-Disk] Split a disco completado. Archivos generados: {generated}. Tiempo: {sw.Elapsed}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telerik-Disk] Error en SplitPdfToDirectoryTelerik: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
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