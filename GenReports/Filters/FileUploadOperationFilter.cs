using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace GenReports.Filters
{
    public class FileUploadOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var fileParameters = context.MethodInfo.GetParameters()
                .Where(p => p.ParameterType == typeof(IFormFile) ||
                            p.ParameterType == typeof(IFormFile[]) ||
                            p.ParameterType == typeof(IEnumerable<IFormFile>))
                .ToList();

            // Verificar si hay parámetros [FromForm]
            var hasFromFormParameters = context.MethodInfo.GetParameters()
                .Any(p => p.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromFormAttribute>() != null);

            // Aplicar el filtro si hay archivos o parámetros [FromForm]
            if (!fileParameters.Any() && !hasFromFormParameters)
                return;

            // Limpiar parámetros existentes
            operation.Parameters?.Clear();

            // Configurar como multipart/form-data
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>(),
                            Required = new HashSet<string>()
                        }
                    }
                }
            };

            var schema = operation.RequestBody.Content["multipart/form-data"].Schema;

            // Agregar propiedades para cada parámetro
            foreach (var parameter in context.MethodInfo.GetParameters())
            {
                var propertyName = parameter.Name ?? "";

                if (parameter.ParameterType == typeof(IFormFile))
                {
                    // Archivo individual
                    schema.Properties[propertyName] = new OpenApiSchema
                    {
                        Type = "string",
                        Format = "binary"
                    };
                }
                else if (parameter.ParameterType == typeof(IFormFile[]) ||
                         parameter.ParameterType == typeof(IEnumerable<IFormFile>))
                {
                    // Múltiples archivos
                    schema.Properties[propertyName] = new OpenApiSchema
                    {
                        Type = "array",
                        Items = new OpenApiSchema
                        {
                            Type = "string",
                            Format = "binary"
                        }
                    };
                }
                else if (parameter.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromFormAttribute>() != null && parameter.ParameterType == typeof(string))
                {
                    // Campo de texto
                    schema.Properties[propertyName] = new OpenApiSchema
                    {
                        Type = "string"
                    };
                }
                else if (parameter.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromFormAttribute>() != null && parameter.ParameterType == typeof(bool))
                {
                    // Campo booleano
                    schema.Properties[propertyName] = new OpenApiSchema
                    {
                        Type = "boolean"
                    };
                }
                else if (parameter.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromFormAttribute>() != null)
                {
                    // Otros tipos
                    schema.Properties[propertyName] = new OpenApiSchema
                    {
                        Type = "string"
                    };
                }

                // Verificar si es requerido
                var requiredAttribute = parameter.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>();
                if (requiredAttribute != null && schema.Properties.ContainsKey(propertyName))
                {
                    schema.Required.Add(propertyName);
                }
            }
        }
    }
}