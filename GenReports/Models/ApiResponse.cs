namespace GenReports.Models
{
    /// <summary>
    /// Clase genérica para envolver las respuestas de la API
    /// </summary>
    /// <typeparam name="T">Tipo de datos que contiene la respuesta</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// Datos de la respuesta
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// Indica si la operación fue exitosa
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Mensaje de la respuesta
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Código de error si aplica
        /// </summary>
        public string? ErrorCode { get; set; }
    }
}