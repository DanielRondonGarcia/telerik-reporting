namespace GenReports.Models
{
    /// <summary>
    /// Resultado de la operación de limpieza de archivos expirados
    /// </summary>
    public class CleanupResult
    {
        /// <summary>
        /// Número de archivos eliminados
        /// </summary>
        public int FilesDeleted { get; set; }

        /// <summary>
        /// Espacio liberado en bytes
        /// </summary>
        public long SpaceFreedBytes { get; set; }

        /// <summary>
        /// Tiempo que tomó la operación de limpieza
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Lista de archivos que no se pudieron eliminar (opcional)
        /// </summary>
        public List<string> FailedDeletions { get; set; } = new List<string>();

        /// <summary>
        /// Indica si la limpieza fue exitosa
        /// </summary>
        public bool Success => FailedDeletions.Count == 0;

        /// <summary>
        /// Espacio liberado en MB
        /// </summary>
        public double SpaceFreedMB => Math.Round(SpaceFreedBytes / (1024.0 * 1024.0), 2);

        /// <summary>
        /// Espacio liberado en GB
        /// </summary>
        public double SpaceFreedGB => Math.Round(SpaceFreedBytes / (1024.0 * 1024.0 * 1024.0), 2);
    }
}