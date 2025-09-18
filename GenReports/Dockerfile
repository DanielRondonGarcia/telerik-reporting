# Consulte https://aka.ms/customizecontainer para aprender a personalizar su contenedor de depuración y cómo Visual Studio usa este Dockerfile para compilar sus imágenes para una depuración más rápida.

# Esta fase se usa cuando se ejecuta desde VS en modo rápido (valor predeterminado para la configuración de depuración)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# Esta fase se usa para compilar el proyecto de servicio
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GenReports.csproj", "GenReports/"]
RUN dotnet restore "./GenReports/GenReports.csproj"
COPY . ./GenReports/
WORKDIR "/src/GenReports"
RUN dotnet build "./GenReports.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Esta fase se usa para publicar el proyecto de servicio que se copiará en la fase final.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GenReports.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Esta fase se usa en producción o cuando se ejecuta desde VS en modo normal (valor predeterminado cuando no se usa la configuración de depuración)
FROM base AS final
WORKDIR /app

# Cambiar temporalmente a root para crear directorios, instalar dependencias y establecer permisos
USER root

# Instalar dependencias de Linux necesarias para SkiaSharp
RUN apt-get update \
    && apt-get install -y --allow-unauthenticated \
    libc6-dev \
    libfreetype6 \
    libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

# Crear directorio para reportes
RUN mkdir -p /app/reports

# Copiar la aplicación publicada
COPY --from=publish /app/publish .

# Copiar plantillas de reportes desde el directorio padre
# Las plantillas están en ../reports/ relativo al Dockerfile
COPY ./GenReports/reports/ /app/reports/

# Establecer permisos para el directorio de reportes
RUN chmod -R 755 /app/reports && \
    chown -R $APP_UID:$APP_UID /app/reports

# Establecer variable de entorno para usar configuración de Docker
ENV ASPNETCORE_ENVIRONMENT=Docker

# Volver al usuario de aplicación
USER $APP_UID

ENTRYPOINT ["dotnet", "GenReports.dll"]
