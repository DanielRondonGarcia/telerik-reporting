# Sistema de Reportes Asíncronos - Optimización para Datasets Grandes

## Problema Resuelto

El sistema anterior tenía limitaciones al manejar reportes con grandes volúmenes de datos (>30,000 registros), causando:
- Timeouts en requests HTTP
- Uso excesivo de memoria
- Bloqueo del servidor durante la generación
- Pérdida de reportes por errores de red

## Solución Implementada

### 1. Cola Asíncrona de Trabajos
- Procesamiento en background usando `BackgroundService`
- Máximo 2 trabajos concurrentes para evitar sobrecarga
- Estado de trabajos en tiempo real
- Limpieza automática de trabajos antiguos

### 2. Múltiples Métodos de Envío

#### A. Multipart/Form-Data (Recomendado para datasets grandes)
```http
POST /api/AsyncReports/queue-multipart
Content-Type: multipart/form-data

reportType: "FacturacionMasiva"
userName: "usuario@empresa.com"
isLargeDataset: true
dataFile: [archivo JSON con los datos]
```

**Ventajas:**
- Soporte hasta 500MB
- Streaming de archivos
- Menor uso de memoria
- Mejor para datasets >30,000 registros

#### B. JSON en Body (Para datasets medianos)
```http
POST /api/AsyncReports/queue-json
Content-Type: application/json

{
  "reportType": "FacturacionMasiva",
  "userName": "usuario@empresa.com",
  "jsonData": "{ ... datos JSON ... }",
  "isLargeDataset": false
}
```

**Ventajas:**
- Más simple para integrar
- Hasta 100MB
- Bueno para datasets <30,000 registros

### 3. Monitoreo y Descarga

#### Verificar Estado del Trabajo
```http
GET /api/AsyncReports/status/{jobId}
```

**Respuesta:**
```json
{
  "jobId": "abc123-def456",
  "status": "Completed",
  "message": "Reporte generado exitosamente",
  "createdAt": "2024-01-15T10:00:00Z",
  "completedAt": "2024-01-15T10:05:30Z",
  "fileName": "reporte_facturacion.zip",
  "fileSizeBytes": 15728640,
  "downloadUrl": "/api/AsyncReports/download/token123",
  "performanceMetrics": {
    "processingTimeMs": 330000,
    "memoryUsageMB": 256,
    "recordsProcessed": 45000
  }
}
```

#### Descargar Archivo
```http
GET /api/AsyncReports/download/{downloadToken}
```

## Ejemplos de Uso

### JavaScript/Fetch - Multipart
```javascript
async function enviarReporteGrande(archivoJSON, tipoReporte, usuario) {
  const formData = new FormData();
  formData.append('reportType', tipoReporte);
  formData.append('userName', usuario);
  formData.append('isLargeDataset', 'true');
  formData.append('dataFile', archivoJSON);

  const response = await fetch('/api/AsyncReports/queue-multipart', {
    method: 'POST',
    body: formData
  });

  const result = await response.json();
  console.log('Trabajo encolado:', result.jobId);
  
  // Monitorear progreso
  return await monitorearTrabajo(result.jobId);
}

async function monitorearTrabajo(jobId) {
  while (true) {
    const response = await fetch(`/api/AsyncReports/status/${jobId}`);
    const status = await response.json();
    
    console.log(`Estado: ${status.status} - ${status.message}`);
    
    if (status.status === 'Completed') {
      console.log(`Descarga disponible: ${status.downloadUrl}`);
      return status.downloadUrl;
    } else if (status.status === 'Failed') {
      throw new Error(`Error en reporte: ${status.message}`);
    }
    
    // Esperar 5 segundos antes de verificar nuevamente
    await new Promise(resolve => setTimeout(resolve, 5000));
  }
}
```

### C# - Cliente
```csharp
public class AsyncReportClient
{
    private readonly HttpClient _httpClient;
    
    public async Task<string> EnviarReporteGrandeAsync(byte[] jsonData, string tipoReporte, string usuario)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(tipoReporte), "reportType");
        content.Add(new StringContent(usuario), "userName");
        content.Add(new StringContent("true"), "isLargeDataset");
        content.Add(new ByteArrayContent(jsonData), "dataFile", "datos.json");

        var response = await _httpClient.PostAsync("/api/AsyncReports/queue-multipart", content);
        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        
        return result.jobId;
    }
    
    public async Task<string> EsperarCompletarAsync(string jobId)
    {
        while (true)
        {
            var response = await _httpClient.GetAsync($"/api/AsyncReports/status/{jobId}");
            var status = await response.Content.ReadFromJsonAsync<dynamic>();
            
            if (status.status == "Completed")
                return status.downloadUrl;
            else if (status.status == "Failed")
                throw new Exception($"Error: {status.message}");
                
            await Task.Delay(5000);
        }
    }
}
```

### Python - Cliente
```python
import requests
import time
import json

def enviar_reporte_grande(archivo_json_path, tipo_reporte, usuario):
    with open(archivo_json_path, 'rb') as f:
        files = {'dataFile': ('datos.json', f, 'application/json')}
        data = {
            'reportType': tipo_reporte,
            'userName': usuario,
            'isLargeDataset': 'true'
        }
        
        response = requests.post('/api/AsyncReports/queue-multipart', 
                               files=files, data=data)
        result = response.json()
        return result['jobId']

def monitorear_trabajo(job_id):
    while True:
        response = requests.get(f'/api/AsyncReports/status/{job_id}')
        status = response.json()
        
        print(f"Estado: {status['status']} - {status['message']}")
        
        if status['status'] == 'Completed':
            return status['downloadUrl']
        elif status['status'] == 'Failed':
            raise Exception(f"Error: {status['message']}")
            
        time.sleep(5)
```

## Configuración Recomendada

### Para Datasets Pequeños (<10,000 registros)
- Usar endpoint JSON tradicional
- Procesamiento síncrono
- Respuesta inmediata

### Para Datasets Medianos (10,000-30,000 registros)
- Usar `/api/AsyncReports/queue-json`
- Procesamiento asíncrono
- Monitoreo cada 5 segundos

### Para Datasets Grandes (>30,000 registros)
- Usar `/api/AsyncReports/queue-multipart`
- Archivo JSON como multipart
- `isLargeDataset: true`
- Monitoreo cada 10 segundos

## Métricas de Rendimiento

El sistema incluye métricas automáticas:
- Tiempo de procesamiento
- Uso de memoria
- Registros procesados
- Tamaño del archivo generado

## Endpoints Adicionales

### Trabajos Activos
```http
GET /api/AsyncReports/active-jobs
```

### Estadísticas del Caché
```http
GET /api/AsyncReports/cache-stats
```

## Beneficios

1. **Escalabilidad**: Maneja datasets de cualquier tamaño
2. **Confiabilidad**: No se pierden reportes por timeouts
3. **Eficiencia**: Procesamiento en background
4. **Monitoreo**: Estado en tiempo real
5. **Flexibilidad**: Múltiples métodos de envío
6. **Optimización**: Caché temporal inteligente

## Migración desde el Sistema Anterior

1. **Datasets pequeños**: Mantener endpoints existentes
2. **Datasets grandes**: Migrar a endpoints asíncronos
3. **Implementación gradual**: Ambos sistemas pueden coexistir
4. **Monitoreo**: Usar métricas para optimizar umbrales