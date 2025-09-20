// ArchiveCompressor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenReports.business
{
    /// <summary>
    /// Representa una entrada (un archivo) para ser incluida en un archivo comprimido.
    /// </summary>
    public record ArchiveEntry(string Name, byte[] Bytes);

    /// <summary>
    /// Representa el resultado de una operación de compresión.
    /// </summary>
    public record ArchiveBuildResult
    {
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
        public string Extension { get; init; } = "zip"; // "7z" o "zip"
        public bool UsedSevenZip { get; init; }
    }

    /// <summary>
    /// Servicio estático centralizado para crear archivos comprimidos.
    /// Intenta usar 7-Zip (7z.exe) con máxima compresión; si no está disponible, cae a ZIP estándar.
    /// </summary>
    public static class ArchiveCompressor
    {
        private const int SevenZipDefaultCompression = 9;

        #region Public API

        /// <summary>
        /// Crea un archivo comprimido a partir de una colección de entradas en memoria.
        /// Prefiere el formato 7z con alta compresión; si 7-Zip no está disponible, utiliza ZIP estándar.
        /// </summary>
        /// <param name="entries">Las entradas de archivo a comprimir.</param>
        /// <param name="sevenZipCompressionLevel">Nivel de compresión para 7z (0-9).</param>
        /// <param name="ct">Token de cancelación.</param>
        public static async Task<ArchiveBuildResult> CreateArchiveAsync(
            IEnumerable<ArchiveEntry> entries,
            int sevenZipCompressionLevel = SevenZipDefaultCompression,
            CancellationToken ct = default)
        {
            var entryList = entries?.ToList() ?? new List<ArchiveEntry>();
            if (entryList.Count == 0)
            {
                return new ArchiveBuildResult();
            }

            if (await IsSevenZipAvailableAsync().ConfigureAwait(false))
            {
                try
                {
                    // Log: "7z disponible: usando compresión 7z."
                    var sevenZipBytes = await CreateSevenZipArchiveAsync(entryList, sevenZipCompressionLevel, ct).ConfigureAwait(false);
                    return new ArchiveBuildResult { Bytes = sevenZipBytes, Extension = "7z", UsedSevenZip = true };
                }
                catch (Exception) // Eliminada la variable 'ex' para quitar la advertencia
                {
                    // Log: $"Error al crear archivo 7z: {ex.Message}. Fallback a ZIP."
                }
            }
            
            // Log: "7z no disponible o falló: usando compresión ZIP estándar."
            var zipBytes = CreateZipArchive(entryList);
            return new ArchiveBuildResult { Bytes = zipBytes, Extension = "zip", UsedSevenZip = false };
        }

        /// <summary>
        /// Crea un archivo comprimido a partir del contenido de un directorio.
        /// Prefiere el formato 7z con alta compresión; si 7-Zip no está disponible, utiliza ZIP estándar.
        /// </summary>
        /// <param name="directoryPath">La ruta al directorio a comprimir.</param>
        /// <param name="sevenZipCompressionLevel">Nivel de compresión para 7z (0-9).</param>
        /// <param name="ct">Token de cancelación.</param>
        public static async Task<ArchiveBuildResult> CreateArchiveFromDirectoryAsync(
            string directoryPath,
            int sevenZipCompressionLevel = SevenZipDefaultCompression,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return new ArchiveBuildResult();
            }

            if (await IsSevenZipAvailableAsync().ConfigureAwait(false))
            {
                try
                {
                    // Log: "7z disponible: usando compresión 7z para el directorio."
                    var sevenZipBytes = await CreateSevenZipArchiveFromDirectoryAsync(directoryPath, sevenZipCompressionLevel, ct).ConfigureAwait(false);
                    return new ArchiveBuildResult { Bytes = sevenZipBytes, Extension = "7z", UsedSevenZip = true };
                }
                catch (Exception) // Eliminada la variable 'ex' para quitar la advertencia
                {
                    // Log: $"Error al crear archivo 7z desde el directorio: {ex.Message}. Fallback a ZIP."
                }
            }
            
            // Log: "7z no disponible o falló: usando compresión ZIP estándar para el directorio."
            var zipBytes = CreateZipArchiveFromDirectory(directoryPath);
            return new ArchiveBuildResult { Bytes = zipBytes, Extension = "zip", UsedSevenZip = false };
        }

        #endregion

        #region ZIP Implementation

        /// <summary>
        /// Crea un archivo ZIP estándar (Deflate, SmallestSize) en memoria desde una lista de entradas.
        /// </summary>
        public static byte[] CreateZipArchive(IEnumerable<ArchiveEntry> entries)
        {
            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var entryData in entries)
                {
                    var entryName = SanitizeEntryName(entryData.Name);
                    var archiveEntry = zipArchive.CreateEntry(entryName, CompressionLevel.SmallestSize);
                    using var entryStream = archiveEntry.Open();
                    entryStream.Write(entryData.Bytes, 0, entryData.Bytes.Length);
                }
            }
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Crea un archivo ZIP estándar en memoria desde un directorio.
        /// </summary>
        public static byte[] CreateZipArchiveFromDirectory(string directoryPath)
        {
            using var memoryStream = new MemoryStream();
            ZipFile.CreateFromDirectory(directoryPath, memoryStream, CompressionLevel.SmallestSize, false);
            return memoryStream.ToArray();
        }

        #endregion
        
        #region 7-Zip Implementation

        /// <summary>
        /// Crea un archivo .7z desde una lista de entradas en memoria.
        /// </summary>
        public static async Task<byte[]> CreateSevenZipArchiveAsync(IEnumerable<ArchiveEntry> entries, int compressionLevel = SevenZipDefaultCompression, CancellationToken ct = default)
        {
            var entryList = entries.ToList();
            if (entryList.Count == 0) return Array.Empty<byte>();

            var tempRoot = Path.Combine(Path.GetTempPath(), "ArchiveCompressor_" + Guid.NewGuid().ToString("N"));
            var workDir = Path.Combine(tempRoot, "in");
            Directory.CreateDirectory(workDir);

            try
            {
                foreach (var entry in entryList)
                {
                    ct.ThrowIfCancellationRequested();
                    var safeName = SanitizeEntryName(entry.Name);
                    var outPath = Path.Combine(workDir, safeName);
                    // Asegurarse de que el subdirectorio exista dentro del directorio temporal
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    await File.WriteAllBytesAsync(outPath, entry.Bytes, ct).ConfigureAwait(false);
                }
                
                return await ExecuteSevenZipProcessAsync(workDir, compressionLevel, ct).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, true); } catch { /* ignore cleanup errors */ }
                }
            }
        }
        
        /// <summary>
        /// Crea un archivo .7z a partir de los archivos de un directorio.
        /// </summary>
        private static async Task<byte[]> CreateSevenZipArchiveFromDirectoryAsync(string directoryPath, int compressionLevel = SevenZipDefaultCompression, CancellationToken ct = default)
        {
            if (!Directory.Exists(directoryPath)) return Array.Empty<byte>();
            
            return await ExecuteSevenZipProcessAsync(directoryPath, compressionLevel, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Ejecuta el proceso 7z.exe para comprimir archivos en un directorio de trabajo.
        /// </summary>
        private static async Task<byte[]> ExecuteSevenZipProcessAsync(string workDir, int compressionLevel, CancellationToken ct)
        {
            var sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                throw new InvalidOperationException("No se pudo encontrar el ejecutable de 7-Zip.");
            }

            var tempArchiveDir = Path.Combine(Path.GetTempPath(), "ArchiveCompressor_out_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempArchiveDir);
            
            try
            {
                var archivePath = Path.Combine(tempArchiveDir, "archive.7z");

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    // "a" (add), "-t7z" (type 7z), "-mx" (compression level), "-m0=lzma2", "-mmt" (multithreading)
                    // ".\*" para añadir todos los archivos del directorio de trabajo.
                    Arguments = $"a -t7z -mx={Math.Clamp(compressionLevel, 0, 9)} -m0=lzma2 -mmt=on \"{archivePath}\" .\\*",
                    WorkingDirectory = workDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var stdErr = new StringBuilder();
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

                if (!proc.Start())
                {
                    throw new InvalidOperationException("No se pudo iniciar el proceso 7z.");
                }

                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException($"7z terminó con código {proc.ExitCode}. Error: {stdErr}");
                }

                return await File.ReadAllBytesAsync(archivePath, ct).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(tempArchiveDir))
                {
                     try { Directory.Delete(tempArchiveDir, true); } catch { /* ignore cleanup errors */ }
                }
            }
        }
        
        #endregion

        #region 7-Zip Availability & Diagnostics

        /// <summary>
        /// Verifica si 7-Zip está disponible y es funcional en el sistema actual.
        /// </summary>
        public static async Task<bool> IsSevenZipAvailableAsync()
        {
            var sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = "--help",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!proc.Start()) return false;
                
                // Usar un CancellationTokenSource para un timeout corto y no bloqueante.
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                return proc.ExitCode == 0;
            }
            catch (OperationCanceledException) { return false; /* Timeout */ }
            catch { return false; /* Otros errores */ }
        }

        /// <summary>
        /// Recopila información de diagnóstico sobre el entorno y la disponibilidad de 7-Zip.
        /// </summary>
        public static async Task<(bool IsAvailable, string DiagnosticInfo)> CheckSevenZipAvailabilityAsync()
        {
            var diagnosticInfo = new StringBuilder();
            diagnosticInfo.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            diagnosticInfo.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            diagnosticInfo.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
            diagnosticInfo.AppendLine($"Is Windows: {RuntimeInformation.IsOSPlatform(OSPlatform.Windows)}");

            var sevenZipPath = ResolveSevenZipPath();
            diagnosticInfo.AppendLine($"7z Resolved Path: {(string.IsNullOrWhiteSpace(sevenZipPath) ? "Not found" : sevenZipPath)}");

            if (!string.IsNullOrWhiteSpace(sevenZipPath) && Path.IsPathRooted(sevenZipPath))
            {
                diagnosticInfo.AppendLine($"7z File Exists: {File.Exists(sevenZipPath)}");
            }
            
            var isWorking = await IsSevenZipAvailableAsync().ConfigureAwait(false);
            diagnosticInfo.AppendLine($"7z Functional: {isWorking}");
            
            return (isWorking, diagnosticInfo.ToString());
        }

        private static string? ResolveSevenZipPath()
        {
            var envPath = Environment.GetEnvironmentVariable("SEVENZIP_EXE_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath)) return envPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var winPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
                };
                return winPaths.FirstOrDefault(File.Exists) ?? "7z.exe"; // Fallback to PATH
            }
            
            // For Linux/macOS
            var unixPaths = new[] { "/usr/bin/7z", "/usr/local/bin/7z", "/bin/7z" };
            return unixPaths.FirstOrDefault(File.Exists) ?? "7z"; // Fallback to PATH
        }

        #endregion
        
        #region Helpers

        /// <summary>
        /// Limpia un nombre de archivo para que sea válido y seguro para usar en un archivo comprimido.
        /// Reemplaza separadores de directorio por '/' y elimina caracteres inválidos.
        /// </summary>
        private static string SanitizeEntryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            
            // Normalizar separadores de ruta a '/' para consistencia en ZIP/7z.
            string normalized = name.Replace(Path.DirectorySeparatorChar, '/');

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitizedParts = normalized.Split('/')
                .Select(part => string.Create(part.Length, part, (span, p) =>
                {
                    p.CopyTo(span);
                    for(int i = 0; i < span.Length; i++)
                    {
                        if (invalidChars.Contains(span[i]))
                        {
                            span[i] = '_';
                        }
                    }
                }));

            return string.Join("/", sanitizedParts);
        }

        #endregion
    }
}