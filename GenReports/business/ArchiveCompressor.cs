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
    public record ArchiveEntry(string Name, byte[] Bytes);

    public class ArchiveBuildResult
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string Extension { get; set; } = "zip"; // "7z" or "zip"
        public bool UsedSevenZip { get; set; }
    }

    /// <summary>
    /// Servicio centralizado para crear archivos comprimidos.
    /// Intenta usar 7-Zip (7z.exe) con máxima compresión; si no está disponible, cae a ZIP estándar.
    /// </summary>
    public class ArchiveCompressor
    {
        /// <summary>
        /// Verifica si 7z está disponible en el sistema actual y proporciona información de diagnóstico.
        /// </summary>
        public async Task<(bool IsAvailable, string DiagnosticInfo)> CheckSevenZipAvailabilityAsync()
        {
            var diagnosticInfo = new StringBuilder();
            
            // Información del entorno
            diagnosticInfo.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            diagnosticInfo.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            diagnosticInfo.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
            diagnosticInfo.AppendLine($"Plataforma: {(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Otro")}");
            diagnosticInfo.AppendLine($"Docker/Linux detectado: {IsRunningInDockerOrLinux()}");
            
            // Variables de entorno relevantes
            var envVars = new[] { "DOTNET_RUNNING_IN_CONTAINER", "DOCKER_CONTAINER", "KUBERNETES_SERVICE_HOST", "SEVENZIP_EXE_PATH" };
            foreach (var envVar in envVars)
            {
                var value = Environment.GetEnvironmentVariable(envVar);
                diagnosticInfo.AppendLine($"{envVar}: {(string.IsNullOrEmpty(value) ? "(no establecida)" : value)}");
            }
            
            // Verificar archivos indicadores de Docker
            diagnosticInfo.AppendLine($"/.dockerenv existe: {File.Exists("/.dockerenv")}");
            if (File.Exists("/proc/1/cgroup"))
            {
                try
                {
                    var cgroup = File.ReadAllText("/proc/1/cgroup");
                    diagnosticInfo.AppendLine($"/proc/1/cgroup contiene 'docker': {cgroup.Contains("docker")}");
                }
                catch
                {
                    diagnosticInfo.AppendLine("/proc/1/cgroup: no se pudo leer");
                }
            }
            
            // Intentar resolver la ruta de 7z
            var sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                diagnosticInfo.AppendLine("7z: No encontrado");
                return (false, diagnosticInfo.ToString());
            }
            
            diagnosticInfo.AppendLine($"7z encontrado en: {sevenZipPath}");
            
            // Verificar si el archivo existe (solo para rutas absolutas)
            if (Path.IsPathRooted(sevenZipPath))
            {
                diagnosticInfo.AppendLine($"Archivo 7z existe: {File.Exists(sevenZipPath)}");
            }
            
            // Verificar si realmente funciona
            var isWorking = await IsSevenZipAvailableAsync(sevenZipPath);
            diagnosticInfo.AppendLine($"7z funcional: {isWorking}");
            
            return (isWorking, diagnosticInfo.ToString());
        }
        /// <summary>
        /// Crea un archivo comprimido preferentemente en formato 7z con LZMA2 al máximo, y si no es posible, cae a ZIP Deflate (SmallestSize).
        /// </summary>
        public async Task<ArchiveBuildResult> CreateArchivePrefer7zAsync(IEnumerable<ArchiveEntry> entries, int sevenZipCompressionLevel = 9, CancellationToken ct = default)
        {
            var list = entries?.ToList() ?? new List<ArchiveEntry>();
            if (list.Count == 0)
            {
                return new ArchiveBuildResult { Bytes = Array.Empty<byte>(), Extension = "zip", UsedSevenZip = false };
            }

            // Intentar 7z primero (ahora funciona en Linux también)
            var (isAvailable, _) = await CheckSevenZipAvailabilityAsync();
            if (isAvailable)
            {
                try
                {
                    Console.WriteLine("7z disponible: usando compresión 7z.");
                    var sevenZipBytes = await CreateSevenZipAsync(list, sevenZipCompressionLevel, ct);
                    return new ArchiveBuildResult { Bytes = sevenZipBytes, Extension = "7z", UsedSevenZip = true };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear archivo 7z: {ex.Message}. Fallback a ZIP.");
                }
            }
            else
            {
                Console.WriteLine("7z no disponible: usando compresión ZIP estándar.");
            }

            var zipBytes = CreateZip(list);
            return new ArchiveBuildResult { Bytes = zipBytes, Extension = "zip", UsedSevenZip = false };
        }

        /// <summary>
        /// Crea un archivo .7z usando 7z.exe con LZMA2 y máxima compresión.
        /// Lanza excepción si 7-Zip no está disponible o falla el proceso.
        /// </summary>
        public async Task<byte[]> CreateSevenZipAsync(IEnumerable<ArchiveEntry> entries, int compressionLevel = 9, CancellationToken ct = default)
        {
            var list = entries.ToList();
            if (list.Count == 0) return Array.Empty<byte>();

            string? sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                var osInfo = RuntimeInformation.OSDescription;
                var isDocker = IsRunningInDockerOrLinux();
                throw new InvalidOperationException($"No se encontró 7z.exe en el sistema. OS: {osInfo}, Docker/Linux: {isDocker}");
            }

            Console.WriteLine($"Intentando usar 7z desde: {sevenZipPath}");

            string tempRoot = Path.Combine(Path.GetTempPath(), "GenReports_7z_" + Guid.NewGuid().ToString("N"));
            string workDir = Path.Combine(tempRoot, "in");
            Directory.CreateDirectory(workDir);

            try
            {
                // Escribir los archivos a comprimir
                foreach (var e in list)
                {
                    ct.ThrowIfCancellationRequested();
                    var safeName = SanitizeFileName(e.Name);
                    var outPath = Path.Combine(workDir, safeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? workDir);
                    await File.WriteAllBytesAsync(outPath, e.Bytes, ct);
                }

                // Crear listfile para evitar problemas con longitud de línea de comandos
                var listFilePath = Path.Combine(workDir, "files.txt");
                var fileLines = Directory.GetFiles(workDir, "*", SearchOption.AllDirectories)
                                          .Select(p => MakeRelativePath(workDir, p))
                                          .ToArray();
                await File.WriteAllLinesAsync(listFilePath, fileLines, Encoding.UTF8, ct);

                var archivePath = Path.Combine(tempRoot, "archive.7z");

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = $"a -t7z -mx={Math.Clamp(compressionLevel, 0, 9)} -m0=lzma2 -ms=on -mmt=on \"{archivePath}\" @\"{listFilePath}\"",
                    WorkingDirectory = workDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!proc.Start())
                {
                    throw new InvalidOperationException("No se pudo iniciar el proceso 7z.exe");
                }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await Task.Run(() => proc.WaitForExit(), ct);

                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException($"7z.exe terminó con código {proc.ExitCode}. Error: {stderr.ToString()}");
                }

                var bytes = await File.ReadAllBytesAsync(archivePath, ct);
                return bytes;
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Crea un ZIP estándar (Deflate, SmallestSize) en memoria.
        /// </summary>
        public byte[] CreateZip(IEnumerable<ArchiveEntry> entries)
        {
            using var ms = new MemoryStream();
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var e in entries)
                {
                    var safe = SanitizeFileName(e.Name);
                    var entry = za.CreateEntry(safe, CompressionLevel.SmallestSize);
                    using var es = entry.Open();
                    es.Write(e.Bytes, 0, e.Bytes.Length);
                }
            }
            return ms.ToArray();
        }

        // --- NUEVOS MÉTODOS: Compresión desde un directorio ---
        /// <summary>
        /// Comprime todos los archivos de un directorio (recursivamente). 
        /// Prefiere 7z con máxima compresión (nivel 9); si falla, usa ZIP estándar como fallback.
        /// </summary>
        public async Task<ArchiveBuildResult> CreateArchiveFromDirectoryPrefer7zAsync(string directory, int sevenZipCompressionLevel = 9, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new ArchiveBuildResult { Bytes = Array.Empty<byte>(), Extension = "zip", UsedSevenZip = false };
            }

            // Intentar 7z primero (ahora funciona en Linux también)
            var (isAvailable, _) = await CheckSevenZipAvailabilityAsync();
            if (isAvailable)
            {
                try
                {
                    Console.WriteLine("7z disponible: usando compresión 7z para directorio.");
                    var compressor = new ArchiveCompressor();
                    var sevenZipBytes = await compressor.CreateSevenZipFromDirectoryInternalAsync(directory, sevenZipCompressionLevel, ct);
                    return new ArchiveBuildResult { Bytes = sevenZipBytes, Extension = "7z", UsedSevenZip = true };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear archivo 7z desde directorio: {ex.Message}. Fallback a ZIP.");
                }
            }
            else
            {
                Console.WriteLine("7z no disponible: usando compresión ZIP estándar para directorio.");
            }

            var zipBytes = CreateZipFromDirectory(directory);
            return new ArchiveBuildResult { Bytes = zipBytes, Extension = "zip", UsedSevenZip = false };
        }

        /// <summary>
        /// Comprime todos los archivos de un directorio (recursivamente) y guarda el resultado en un archivo. Prefiere 7z; si falla, usa ZIP estándar.
        /// </summary>
        public static async Task<(string filePath, string extension)> CreateArchiveFromDirectoryPrefer7zAsync(string directory, string outputPath)
        {
            if (!Directory.Exists(directory) || !Directory.GetFileSystemEntries(directory).Any())
            {
                return (outputPath, "zip");
            }

            // Intentar 7z primero (ahora funciona en Linux también)
            var sevenZipPath = ResolveSevenZipPath();
            if (!string.IsNullOrEmpty(sevenZipPath) && await IsSevenZipAvailableAsync(sevenZipPath))
            {
                try
                {
                    Console.WriteLine("7z disponible: usando compresión 7z para directorio.");
                    return await CreateSevenZipFromDirectoryAsync(directory, outputPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear archivo 7z desde directorio: {ex.Message}. Fallback a ZIP.");
                }
            }
            else
            {
                Console.WriteLine("7z no disponible: usando compresión ZIP estándar para directorio.");
            }

            return await CreateZipFromDirectoryAsync(directory, outputPath);
        }

        /// <summary>
        /// Crea un archivo .7z desde un directorio y lo guarda en la ruta especificada.
        /// </summary>
        public static async Task<(string filePath, string extension)> CreateSevenZipFromDirectoryAsync(string directory, string outputPath)
        {
            var compressor = new ArchiveCompressor();
            var bytes = await compressor.CreateSevenZipFromDirectoryInternalAsync(directory);
            
            var finalPath = Path.ChangeExtension(outputPath, ".7z");
            await File.WriteAllBytesAsync(finalPath, bytes);
            return (finalPath, "7z");
        }

        /// <summary>
        /// Crea un archivo ZIP desde un directorio y lo guarda en la ruta especificada.
        /// </summary>
        public static async Task<(string filePath, string extension)> CreateZipFromDirectoryAsync(string directory, string outputPath)
        {
            var compressor = new ArchiveCompressor();
            var bytes = compressor.CreateZipFromDirectory(directory);
            
            var finalPath = Path.ChangeExtension(outputPath, ".zip");
            await File.WriteAllBytesAsync(finalPath, bytes);
            return (finalPath, "zip");
        }

        /// <summary>
        /// Crea un archivo .7z tomando los archivos existentes en un directorio.
        /// </summary>
        public async Task<byte[]> CreateSevenZipFromDirectoryInternalAsync(string directory, int compressionLevel = 9, CancellationToken ct = default)
        {
            if (!Directory.Exists(directory)) return Array.Empty<byte>();

            string? sevenZipPath = ResolveSevenZipPath();
            if (string.IsNullOrWhiteSpace(sevenZipPath))
            {
                var osInfo = RuntimeInformation.OSDescription;
                var isDocker = IsRunningInDockerOrLinux();
                throw new InvalidOperationException($"No se encontró 7z.exe en el sistema. OS: {osInfo}, Docker/Linux: {isDocker}");
            }

            Console.WriteLine($"Intentando usar 7z desde: {sevenZipPath} para directorio: {directory}");

            string tempRoot = Path.Combine(Path.GetTempPath(), "GenReports_7zdir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var archivePath = Path.Combine(tempRoot, "archive.7z");

                // Crear listfile con rutas relativas desde 'directory'
                var listFilePath = Path.Combine(tempRoot, "files.txt");
                var fileLines = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                                          .Select(p => MakeRelativePath(directory, p))
                                          .ToArray();
                await File.WriteAllLinesAsync(listFilePath, fileLines, Encoding.UTF8, ct);

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = $"a -t7z -mx={Math.Clamp(compressionLevel, 0, 9)} -m0=lzma2 -ms=on -mmt=on \"{archivePath}\" @\"{listFilePath}\"",
                    WorkingDirectory = directory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!proc.Start())
                {
                    throw new InvalidOperationException("No se pudo iniciar el proceso 7z.exe");
                }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await Task.Run(() => proc.WaitForExit(), ct);

                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException($"7z.exe terminó con código {proc.ExitCode}. Error: {stderr.ToString()}");
                }

                var bytes = await File.ReadAllBytesAsync(archivePath, ct);
                return bytes;
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Crea un ZIP estándar en memoria desde un directorio (recursivo). Copia por streams para no cargar archivos completos en memoria.
        /// </summary>
        public byte[] CreateZipFromDirectory(string directory)
        {
            using var ms = new MemoryStream();
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var relative = MakeRelativePath(directory, file);
                    var sanitized = string.Join('/', relative
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Select(SanitizeFileName));

                    var entry = za.CreateEntry(sanitized, CompressionLevel.SmallestSize);
                    using var es = entry.Open();
                    using var fs = File.OpenRead(file);
                    fs.CopyTo(es);
                }
            }
            return ms.ToArray();
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "file.bin";
            var justName = name.Replace('\\', '/');
            justName = string.Join('/', justName.Split('/').Select(Path.GetFileName));
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                justName = justName.Replace(c, '_');
            }
            return justName;
        }

        private static string MakeRelativePath(string baseDir, string fullPath)
        {
            var b = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var uri = new Uri(b, UriKind.Absolute);
            var f = new Uri(fullPath, UriKind.Absolute);
            var rel = uri.MakeRelativeUri(f).ToString();
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Detecta si estamos ejecutándose en un contenedor Docker o entorno Linux.
        /// </summary>
        private static bool IsRunningInDockerOrLinux()
        {
            try
            {
                // Verificar si estamos en Linux
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return true;
                }

                // Verificar indicadores comunes de Docker en Windows
                var dockerIndicators = new[]
                {
                    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                    Environment.GetEnvironmentVariable("DOCKER_CONTAINER"),
                    Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")
                };

                if (dockerIndicators.Any(indicator => !string.IsNullOrEmpty(indicator)))
                {
                    return true;
                }

                // Verificar si existe el archivo /.dockerenv (indicador común de Docker)
                if (File.Exists("/.dockerenv"))
                {
                    return true;
                }

                // Verificar en el archivo /proc/1/cgroup si contiene "docker" (solo en Linux)
                if (File.Exists("/proc/1/cgroup"))
                {
                    try
                    {
                        var cgroup = File.ReadAllText("/proc/1/cgroup");
                        if (cgroup.Contains("docker") || cgroup.Contains("containerd"))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignorar errores al leer /proc/1/cgroup
                    }
                }

                return false;
            }
            catch
            {
                // En caso de error, asumir que no estamos en Docker
                return false;
            }
        }

        /// <summary>
        /// Verifica si 7z está realmente disponible ejecutando una prueba simple.
        /// </summary>
        private static async Task<bool> IsSevenZipAvailableAsync(string sevenZipPath)
        {
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

                using var proc = new Process { StartInfo = psi };
                if (!proc.Start())
                {
                    return false;
                }

                // Dar un timeout corto para la verificación
                var completed = await Task.Run(() => proc.WaitForExit(3000));
                if (!completed)
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string? ResolveSevenZipPath()
        {
            // 1) Variable de entorno personalizada (funciona en cualquier OS)
            var env = Environment.GetEnvironmentVariable("SEVENZIP_EXE_PATH");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            // 2) Detectar según el sistema operativo
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Rutas comunes en Windows
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return c;
                }
                // En Windows, intentar 7z.exe en PATH
                return "7z";
            }
            else
            {
                // En Linux/Unix, buscar 7z (instalado por p7zip-full)
                var linuxCandidates = new[]
                {
                    "/usr/bin/7z",
                    "/usr/local/bin/7z",
                    "/bin/7z"
                };
                foreach (var c in linuxCandidates)
                {
                    if (File.Exists(c)) return c;
                }
                // Intentar 7z en PATH (común después de instalar p7zip-full)
                return "7z";
            }
        }
    }
}