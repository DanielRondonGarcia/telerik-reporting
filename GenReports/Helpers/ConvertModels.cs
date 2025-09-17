using GenReports.business;
using GenReports.Models;

namespace GenReports.Helpers
{
    /// <summary>
    /// Clase helper para convertir entre diferentes modelos
    /// </summary>
    public class ConvertModels
    {
        /// <summary>
        /// Convierte un ArchivoResult a UFile
        /// </summary>
        /// <param name="archivo">Objeto ArchivoResult a convertir</param>
        /// <returns>Objeto UFile convertido</returns>
        public UFile ConvertToFile(ArchivoResult archivo)
        {
            if (archivo == null)
                throw new ArgumentNullException(nameof(archivo));

            var uFile = new UFile
            {
                NombreArchivo = archivo.NombreArchivo,
                BytesArchivo = archivo.BytesArchivo,
                Usuario = archivo.Usuario,
                FechaGeneracion = archivo.FechaGeneracion
            };

            // Detectar automáticamente el tipo de contenido
            uFile.DetectContentType();

            return uFile;
        }
    }
}