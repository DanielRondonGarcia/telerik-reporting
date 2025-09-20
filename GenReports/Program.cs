using GenReports.Models;
using GenReports.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configurar opciones de caché temporal
builder.Services.Configure<TemporaryFileCacheOptions>(
    builder.Configuration.GetSection("TemporaryFileCache"));

// Configurar opciones de reportes
builder.Services.Configure<ReportsConfiguration>(
    builder.Configuration.GetSection("ReportsConfiguration"));

// Registrar servicio de caché temporal
builder.Services.AddSingleton<ITemporaryFileCacheService, TemporaryFileCacheService>();

// Registrar servicio de claves de caché globales
builder.Services.AddSingleton<IGlobalCacheKeyService, GlobalCacheKeyService>();

// Registrar servicio de reportes
builder.Services.AddScoped<GenReports.business.Report>();

// Registrar servicio de background para limpieza automática
builder.Services.AddHostedService<TemporaryFileCacheCleanupService>();

// Registrar servicio de cola de reportes como singleton e iniciarlo como hosted service
builder.Services.AddSingleton<ReportQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReportQueueService>());

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Version = "v1",
        Title = "GenReports API",
        Description = "API para generación de reportes usando Telerik",
        TermsOfService = new Uri("https://example.com/terms"),
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "GenReports Team",
            Email = "support@genreports.com",
            Url = new Uri("https://example.com/contact")
        },
        License = new Microsoft.OpenApi.Models.OpenApiLicense
        {
            Name = "MIT License",
            Url = new Uri("https://example.com/license")
        }
    });

    // Configurar esquemas de seguridad si es necesario
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Habilitar anotaciones de Swagger
    options.EnableAnnotations();

    // Incluir comentarios XML si existe
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// SkiaSharp graphics engine is enabled via Telerik.Drawing.Skia package for Linux compatibility
Console.WriteLine("Using Skia graphics engine for Telerik Reporting on Linux");

var app = builder.Build();

// Configure the HTTP request pipeline.
// Habilitar Swagger en todos los entornos para monitoreo y health checks
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GenReports API v1");
    c.RoutePrefix = "swagger"; // Swagger estará disponible en /swagger
    
    // En producción, configurar opciones adicionales de seguridad si es necesario
    if (!app.Environment.IsDevelopment())
    {
        c.DocumentTitle = "GenReports API - Production";
        // Opcional: Agregar autenticación básica o restricciones de IP aquí
    }
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();