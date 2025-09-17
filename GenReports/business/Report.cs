using System.Text.Json;
using Telerik.Reporting;
using Telerik.Reporting.Processing;
using ICSharpCode.SharpZipLib.Zip;
using System.IO.Compression;

namespace GenReports.business
{
    public class Report
    {
        private readonly string _directorioTemporal;
        private readonly string _urlBaseReportes;

        public Report(string directorioTemporal = @"C:\temp\", string urlBaseReportes = "")
        {
            _directorioTemporal = directorioTemporal;
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
                // Ruta de la plantilla del reporte
                var plantillaPath = @"C:\Listados\GEN\REPORTES\telerik\GEN_INFO_USUARIO_T.json.batch.trdp";

                // Verificar que existe la plantilla
                if (!File.Exists(plantillaPath))
                {
                    throw new FileNotFoundException($"No se encuentra el archivo de plantilla del reporte: {plantillaPath}");
                }

                // Crear el procesador de reportes
                var telerikReportProcessor = new ReportProcessor();
                
                // Configurar información del dispositivo
                var deviceInfo = new System.Collections.Hashtable();
                
                // Cargar la definición del reporte desde el archivo .trdp
                var reportPackager = new ReportPackager();
                Telerik.Reporting.Report reportDefinition;
                
                using (var fs = new FileStream(plantillaPath, FileMode.Open, FileAccess.Read))
                {
                    reportDefinition = (Telerik.Reporting.Report)reportPackager.UnpackageDocument(fs);
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
                var resultado = telerikReportProcessor.RenderReport(formatoSalida, instanceReportSource, deviceInfo);

                // Verificar errores
                if (resultado.HasErrors)
                {
                    var errores = string.Join("; ", resultado.Errors.Select(e => e.Message));
                    Console.WriteLine($"Errores en la generación del reporte: {errores}");
                    
                    if (resultado.DocumentBytes == null)
                    {
                        throw new InvalidOperationException($"Error generando el reporte: {errores}");
                    }
                }

                // Crear el archivo de salida
                var nombreArchivo = $"{_directorioTemporal}Reporte_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                
                var archivoSalida = new ArchivoResult
                {
                    NombreArchivo = nombreArchivo,
                    BytesArchivo = resultado.DocumentBytes,
                    Usuario = userName,
                    FechaGeneracion = DateTime.Now
                };

                return archivoSalida;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en GenerateTelerik: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configura el origen de datos del reporte
        /// </summary>
        /// <param name="reportDefinition">Definición del reporte</param>
        /// <param name="reportData">Datos del reporte</param>
        private void ConfigureDataSource(Telerik.Reporting.Report reportDefinition, List<object> reportData)
        {
            // Convertir los datos a JSON string
            var jsonString = JsonSerializer.Serialize(reportData);
            
            // Buscar el JsonDataSource en el reporte
            var jsonDataSource = reportDefinition.DataSource as JsonDataSource;
            
            if (jsonDataSource != null)
            {
                // Asignar los datos como JSON string al JsonDataSource
                jsonDataSource.Source = jsonString;
            }
            else
            {
                // Si no hay JsonDataSource, crear uno nuevo
                var newJsonDataSource = new JsonDataSource
                {
                    Source = jsonString
                };
                reportDefinition.DataSource = newJsonDataSource;
            }
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

                // Crear el archivo de salida comprimido
                var nombreArchivoZip = $"{_directorioTemporal}Reportes_{reportType}_Batch_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                
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
