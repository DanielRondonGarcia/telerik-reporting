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
   docker run -d --rm --name genreports -p 8080:8080 -p 8081:8081 genreports:prod
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
