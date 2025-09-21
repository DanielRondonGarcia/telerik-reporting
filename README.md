# Telerik Reporting API

API para generación de reportes usando Telerik Reporting con configuración parametrizada para compatibilidad Windows/Docker.

## Configuración Parametrizada de Reportes

### Resumen de Cambios

Se ha implementado una configuración parametrizada para las rutas de reportes que permite compatibilidad tanto con Windows como con Docker.

### Archivos Modificados

#### 1. `appsettings.json`

```json
{
  "ReportsConfiguration": {
    "BasePath": "reports",
    "TemporaryDirectory": "${TMP}"
  }
}
```

#### 2. `appsettings.Development.json`

```json
{
  "ReportsConfiguration": {
    "BasePath": "reports",
    "TemporaryDirectory": "${TMP}"
  }
}
```

#### 3. `Models/ReportsConfiguration.cs` (Nuevo)

Clase de configuración para mapear las configuraciones desde appsettings.

#### 4. `business/Report.cs`

- Agregado constructor que acepta `IOptions<ReportsConfiguration>`
- Reemplazada ruta hardcodeada por configuración parametrizada
- Mantenido constructor original para compatibilidad hacia atrás

#### 5. `Program.cs`

- Registrada configuración `ReportsConfiguration`
- Registrado servicio `Report` como Scoped

#### 6. `Controllers/ReportesController.cs`

- Actualizado para usar inyección de dependencias del servicio `Report`

#### 7. `Dockerfile`

- Agregada creación del directorio `/app/reports`
- Agregada copia de plantillas desde directorio local `reports/`
- Establecidos permisos apropiados

### Estructura de Directorios

```
telerik-reporting/
├── reports/                    # Directorio para plantillas .trdp
│   ├── README.md              # Documentación del directorio
│   ├── .gitkeep              # Mantiene el directorio en git
│   └── *.trdp                # Plantillas de reportes (agregar aquí)
└── GenReports/
    ├── Models/
    │   └── ReportsConfiguration.cs
    └── ...
```

## Uso

### Configuración Multiplataforma

1. La configuración por defecto usa variables de entorno multiplataforma:

   ```json
   {
     "ReportsConfiguration": {
       "BasePath": "reports",
       "TemporaryDirectory": "${TMP}"
     }
   }
   ```

2. **Windows**: `${TMP}` apunta a `%TEMP%` (ej: `C:\Users\Usuario\AppData\Local\Temp`)
3. **Linux/Docker**: `${TMP}` apunta a `/tmp`
4. Colocar las plantillas .trdp en el directorio `reports/` del proyecto

### En Docker

1. Colocar las plantillas .trdp en el directorio `reports/` del proyecto
2. Publicar la aplicación en Release (esto genera `./GenReports/bin/Release/net8.0/publish`):

   ```bash
   dotnet publish ./GenReports/GenReports.csproj -c Release -o ./GenReports/bin/Release/net8.0/publish
   ```

3. Construir la imagen usando el Dockerfile funcional (`Dockerfile.prod`) con contexto en `GenReports/`:

   ```bash
   docker build -t genreports:prod -f GenReports/Dockerfile.prod GenReports
   ```

4. Ejecutar el contenedor exponiendo los puertos 8080 y 8081:

   ```bash
   docker run -d --rm --name genreports -p 5222:8080 -p 8081:8081 genreports:prod
   ```

5. La aplicación usará automáticamente `/app/reports/` como ruta base

## Desarrollo

### Requisitos

- .NET 8.0 o superior
- Telerik Reporting

### Ejecutar localmente

```bash
cd GenReports
dotnet run
```

### Construir imagen Docker

```bash
# 1) Publicar
dotnet publish ./GenReports/GenReports.csproj -c Release -o ./GenReports/bin/Release/net8.0/publish

# 2) Construir imagen con Dockerfile.prod (contexto GenReports)
docker build -t genreports:prod -f GenReports/Dockerfile.prod GenReports

# 3) Ejecutar contenedor
docker run -d --rm --name genreports -p 8080:8080 -p 8081:8081 genrereports:prod
```

## Ventajas de la Configuración Parametrizada

1. **Flexibilidad**: Diferentes rutas para diferentes entornos
2. **Compatibilidad Docker**: Rutas Linux para contenedores
3. **Mantenibilidad**: Configuración centralizada en appsettings
4. **Retrocompatibilidad**: Constructor original mantenido
5. **Inyección de Dependencias**: Mejor testabilidad y mantenimiento

## Migración

Para migrar plantillas existentes:

1. **Windows**: Copiar archivos .trdp a la ruta configurada en `appsettings.json`
2. **Docker**: Copiar archivos .trdp al directorio `reports/` del proyecto

## Notas Importantes

- El directorio temporal usa la variable de entorno `${TMP}` que es multiplataforma
- **Windows**: `${TMP}` se resuelve automáticamente a `%TEMP%`
- **Linux/Docker**: `${TMP}` se resuelve automáticamente a `/tmp`
- Las plantillas se almacenan en el directorio `reports/` relativo al proyecto
- El Dockerfile copia automáticamente el contenido del directorio `reports/`

# Manejo de Licencia de Telerik en Docker

Este documento explica cómo manejar la licencia de Telerik en builds de Docker para separar código abierto de despliegues con licencia.

## Problema Resuelto

- **Código abierto**: No se puede distribuir una imagen Docker con licencia de Telerik
- **Despliegues privados**: Necesitan la licencia para funcionar correctamente
- **Solución**: Dockerfile que acepta la licencia como argumento de build opcional

## Configuración del Dockerfile

El Dockerfile ahora acepta un argumento `TELERIK_LICENSE_KEY` que es opcional:

```dockerfile
ARG TELERIK_LICENSE_KEY=""
ENV TELERIK_LICENSE_KEY=$TELERIK_LICENSE_KEY
```

## Uso

### 1. Build Público (sin licencia)

```bash
# Build normal para código abierto
docker build -t genreports:public -f GenReports/Dockerfile .
```

### 2. Build Privado (con licencia)

```bash
# Build con licencia para despliegue
docker build -t genreports:licensed \
  --build-arg TELERIK_LICENSE_KEY="tu-licencia-aqui" \
  -f GenReports/Dockerfile .
```

### 3. Docker Compose (con licencia)

```yaml
version: '3.8'
services:
  genreports:
    build:
      context: .
      dockerfile: GenReports/Dockerfile
      args:
        TELERIK_LICENSE_KEY: ${TELERIK_LICENSE_KEY}
    environment:
      - TELERIK_LICENSE_KEY=${TELERIK_LICENSE_KEY}
```

## Workflows de GitHub Actions

### Workflow Principal (ci.yml)

- **Propósito**: Build público para código abierto
- **Características**:
  - NO incluye licencia de Telerik
  - Se ejecuta automáticamente en push a main
  - Imagen pública en registry

### Workflow de Despliegue (deploy-with-license.yml.example)

- **Propósito**: Despliegue con licencia
- **Características**:
  - Incluye licencia de Telerik desde secrets
  - Ejecución manual (workflow_dispatch)
  - Para entornos staging/production

## Configuración de Secrets

Para usar el workflow de despliegue con licencia:

1. Ir a Settings > Secrets and variables > Actions
2. Agregar secret: `TELERIK_LICENSE_KEY`
3. Renombrar `deploy-with-license.yml.example` a `deploy-with-license.yml`

## Variables de Entorno en Runtime

La aplicación puede acceder a la licencia mediante:

```csharp
var telerikLicense = Environment.GetEnvironmentVariable("TELERIK_LICENSE_KEY");
```

## Kubernetes

Para despliegues en Kubernetes con licencia:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: genreports
spec:
  template:
    spec:
      containers:
      - name: genreports
        image: genreports:licensed
        env:
        - name: TELERIK_LICENSE_KEY
          valueFrom:
            secretKeyRef:
              name: telerik-license
              key: license-key
```

# Telerik License Setup Guide

## Overview

This guide explains how to configure the Telerik license for the GenReports API deployment on `telerik.rondon.cloud`.

## Required Secrets

### GitHub Actions Secrets

You need to create the following secrets in your GitHub repository:

#### Secret para Feed de NuGet

**Secret Name:** `TELERIK_NUGET_KEY`
**Description:** Tu clave del feed privado de NuGet de Telerik (para `dotnet restore`)

#### Secret para Licencia de Runtime

**Secret Name:** `TELERIK_LICENSE_KEY`
**Description:** Tu clave de licencia de Telerik (para la aplicación en runtime)

### How to Add GitHub Secrets

1. Go to your repository on GitHub
2. Navigate to Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Add both secrets:
   - Name: `TELERIK_NUGET_KEY`, Value: Your Telerik NuGet feed key
   - Name: `TELERIK_LICENSE_KEY`, Value: Your Telerik license key
5. Click "Add secret" for each

## Kubernetes Secret

The CI workflow automatically creates a Kubernetes secret named `telerik-license-secret` in the `devsecops` namespace with the following configuration:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: telerik-license-secret
  namespace: devsecops
type: Opaque
data:
  license-key: <base64-encoded-license-key>
```

## Environment Variable in Pod

The deployment configuration automatically injects the license as an environment variable:

```yaml
env:
- name: TELERIK_LICENSE_KEY
  valueFrom:
    secretKeyRef:
      name: telerik-license-secret
      key: license-key
```

## Manual Secret Creation (if needed)

If you need to create the secret manually:

```bash
kubectl create secret generic telerik-license-secret \
  --namespace=devsecops \
  --from-literal=license-key="YOUR_TELERIK_LICENSE_KEY"
```

## Verification

To verify the secret is created correctly:

```bash
# Check if secret exists
kubectl get secrets -n devsecops | grep telerik-license-secret

# View secret details (without revealing the value)
kubectl describe secret telerik-license-secret -n devsecops
```

## Application Configuration

The application will automatically read the `TELERIK_LICENSE_KEY` environment variable. Make sure your application code is configured to use this environment variable for Telerik license initialization.

## Security Notes

- Never commit license keys to version control
- The license key is stored as a Kubernetes secret and injected at runtime
- GitHub Actions secrets are encrypted and only accessible during workflow execution
- The secret is automatically created/updated during each deployment

## Deployment URL

Once deployed, the application will be available at: **<https://telerik.rondon.cloud>**

# Configuración de NuGet para Telerik Reporting

## Descripción General

Este documento explica la configuración del archivo `nuget.config` para asegurar que los paquetes de Telerik se restauren correctamente tanto en desarrollo local como en el pipeline de CI/CD.

## Configuraciones Implementadas

### 1. Fuentes de Paquetes

- **nuget.org**: Fuente principal para paquetes públicos de .NET
- **telerik**: Fuente privada de Telerik para paquetes de Reporting
- **protocolVersion="3"**: Especifica el protocolo v3 para mejor rendimiento

### 2. Credenciales de Telerik

```xml
<packageSourceCredentials>
  <telerik>
    <add key="Username" value="api-key" />
    <add key="ClearTextPassword" value="%TELERIK_NUGET_KEY%" />
  </telerik>
</packageSourceCredentials>
```

**IMPORTANTE**: La clave se inyecta desde la variable de entorno `TELERIK_NUGET_KEY`, no está hardcodeada en el archivo por seguridad.

### 3. Configuraciones de Seguridad

- **signatureValidationMode**: Requiere validación de firmas
- **trustedSigners**: Define Telerik y NuGet.org como firmantes confiables
- **Certificados**: Fingerprints específicos para validación

### 4. Configuraciones de Rendimiento

- **globalPackagesFolder**: Carpeta local para caché de paquetes
- **http_proxy.timeout**: Timeout extendido para descargas de Telerik
- **repositoryPath**: Ruta específica para paquetes

## Configuración para Desarrollo Local

Para trabajar en desarrollo local, necesitas configurar la variable de entorno `TELERIK_NUGET_KEY`:

### Windows (PowerShell)

```powershell
$env:TELERIK_NUGET_KEY="tu_clave_del_feed_de_telerik_aqui"
```

### Windows (CMD)

```cmd
set TELERIK_NUGET_KEY=tu_clave_del_feed_de_telerik_aqui
```

### Linux/macOS

```bash
export TELERIK_NUGET_KEY="tu_clave_del_feed_de_telerik_aqui"
```

### Visual Studio / Rider

Puedes configurar la variable en las propiedades del proyecto o en el archivo `launchSettings.json`:

```json
{
  "profiles": {
    "GenReports": {
      "environmentVariables": {
        "TELERIK_NUGET_KEY": "tu_clave_del_feed_de_telerik_aqui"
      }
    }
  }
}
```

## Flujo en CI/CD

### Durante el Build de Docker

1. El `nuget.config` se copia al contenedor
2. Se usa la clave encriptada del archivo para desarrollo
3. En producción, la variable `TELERIK_LICENSE_KEY` sobrescribe la configuración

### Durante el Despliegue

1. El secreto `TELERIK_LICENSE_KEY` se inyecta desde GitHub Secrets
2. Kubernetes crea el secreto `telerik-license-secret`
3. La aplicación usa la variable de entorno en runtime

## Variables de Entorno Importantes

| Variable | Descripción | Origen | Uso |
|----------|-------------|---------|-----|
| `TELERIK_NUGET_KEY` | Clave del feed privado de NuGet de Telerik | GitHub Secrets | Build time (dotnet restore) |
| `TELERIK_LICENSE_KEY` | Clave de licencia de Telerik | GitHub Secrets | Runtime (aplicación) |
| `BUILD_CONFIGURATION` | Configuración de build (Release/Debug) | CI/CD Pipeline | Build time |

## Troubleshooting

### Error: "Unable to load the service index for source"

- Verificar conectividad a `https://nuget.telerik.com/v3/index.json`
- Validar que la clave de Telerik sea válida
- Revisar configuración de proxy si aplica

### Error: "Package signature validation failed"

- Verificar que los certificados en `trustedSigners` estén actualizados
- Confirmar que `signatureValidationMode` esté configurado correctamente

### Error: "Authentication failed"

- Verificar que `TELERIK_LICENSE_KEY` esté configurado en GitHub Secrets
- Confirmar que la clave no haya expirado
- Validar formato de la clave (debe ser la API key de Telerik)

## Mantenimiento

### Actualización de Certificados

Los fingerprints de certificados pueden cambiar. Verificar periódicamente:


### Renovación de Licencia

Cuando la licencia de Telerik expire:

1. Obtener nueva API key desde el portal de Telerik
2. Actualizar el secreto `TELERIK_LICENSE_KEY` en GitHub
3. Opcionalmente, actualizar la clave encriptada en `nuget.config` para desarrollo local

## Seguridad

- ✅ Las claves están encriptadas en el archivo de configuración
- ✅ Las claves sensibles se manejan como secretos en GitHub
- ✅ Los certificados están validados para prevenir ataques man-in-the-middle
- ✅ Solo fuentes confiables están configuradas

## Referencias

- [Documentación oficial de NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file)
- [Telerik NuGet Feed Documentation](https://docs.telerik.com/reporting/installation-and-deployment/nuget)
- [GitHub Secrets Documentation](https://docs.github.com/en/actions/security-guides/encrypted-secrets)

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

1. **Datasets pequeños**: Mantener endpoints existentes.
2. **Datasets grandes**: Migrar a endpoints asíncronos.
3. **Implementación gradual**: Ambos sistemas pueden coexistir.
4. **Monitoreo**: Usar métricas para optimizar umbrales.
