# Directorio de Plantillas de Reportes

Este directorio contiene las plantillas de reportes (.trdp) que utiliza la aplicación GenReports.

## Configuración

La ruta base de los reportes se configura en `appsettings.json`:

```json
{
  "ReportsConfiguration": {
    "BasePath": "C:\\Listados\\GEN\\REPORTES\\telerik",
    "TemporaryDirectory": "C:\\temp\\"
  }
}
```

Para Docker, se utiliza `appsettings.Development.json`:

```json
{
  "ReportsConfiguration": {
    "BasePath": "/app/reports",
    "TemporaryDirectory": "/tmp/"
  }
}
```

## Uso en Docker

1. Coloca tus archivos de plantillas (.trdp) en este directorio `reports/`
2. Al construir la imagen Docker, estos archivos se copiarán automáticamente a `/app/reports/`
3. La aplicación utilizará la configuración de `appsettings.Development.json` en el contenedor

## Archivos de Plantilla

Coloca aquí tus archivos .trdp, por ejemplo:
- `GEN_INFO_USUARIO_T.json.batch.trdp`
- Otros archivos de plantillas de reportes

## Notas

- En Windows, la aplicación usará la ruta configurada en `appsettings.json`
- En Docker, la aplicación usará `/app/reports/` como se define en `appsettings.Development.json`
- El directorio temporal también es configurable para cada entorno