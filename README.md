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
    "BasePath": "C:\\Listados\\GEN\\REPORTES\\telerik",
    "TemporaryDirectory": "C:\\temp\\"
  }
}
```

#### 2. `appsettings.Development.json`
```json
{
  "ReportsConfiguration": {
    "BasePath": "/app/reports",
    "TemporaryDirectory": "/tmp/"
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

### En Windows (Desarrollo/Producción)
1. Configurar la ruta en `appsettings.json`:
   ```json
   {
     "ReportsConfiguration": {
       "BasePath": "C:\\Listados\\GEN\\REPORTES\\telerik",
       "TemporaryDirectory": "C:\\temp\\"
     }
   }
   ```

2. Colocar las plantillas .trdp en la ruta configurada

### En Docker
1. Colocar las plantillas .trdp en el directorio `reports/` del proyecto
2. Construir la imagen Docker:
   ```bash
   docker build -t genreports .
   ```
3. La aplicación usará automáticamente `/app/reports/` como ruta base

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
docker build -t genreports .
docker run -p 8080:8080 genreports
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

- El directorio temporal también es configurable por entorno
- Las rutas en Windows usan doble backslash (`\\`) en JSON
- Las rutas en Docker usan forward slash (`/`)
- El Dockerfile copia automáticamente el contenido del directorio `reports/`