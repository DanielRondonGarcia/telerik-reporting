using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        /// Crea un archivo comprimido preferentemente en formato 7z con LZMA2 al máximo, y si no es posible, cae a ZIP Deflate (SmallestSize).
        /// </summary>
        public async Task<ArchiveBuildResult> CreateArchivePrefer7zAsync(IEnumerable<ArchiveEntry> entries, int sevenZipCompressionLevel = 9, CancellationToken ct = default)
        {
            var list = entries?.ToList() ?? new List<ArchiveEntry>();
            if (list.Count == 0)
            {
                return new ArchiveBuildResult { Bytes = Array.Empty<byte>(), Extension = "7z", UsedSevenZip = true };
            }

            try
            {
                var sevenZipBytes = await CreateSevenZipAsync(list, sevenZipCompressionLevel, ct);
                return new ArchiveBuildResult { Bytes = sevenZipBytes, Extension = "7z", UsedSevenZip = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Advertencia: No se pudo crear 7z, usando ZIP estándar. Detalle: {ex.Message}");
                var zipBytes = CreateZip(list);
                return new ArchiveBuildResult { Bytes = zipBytes, Extension = "zip", UsedSevenZip = false };
            }
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
                throw new InvalidOperationException("No se encontró 7z.exe en el sistema.");
            }

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

        private static string? ResolveSevenZipPath()
        {
            // 1) Variable de entorno personalizada
            var env = Environment.GetEnvironmentVariable("SEVENZIP_EXE_PATH");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            // 2) Rutas comunes en Windows
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // 3) Confiar en PATH
            return "7z";
        }
    }
}