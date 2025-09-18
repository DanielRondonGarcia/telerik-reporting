using GenReports.Models;
using GenReports.Services;
using GenReports.business;
using Telerik.Reporting.Processing;

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

// Registrar servicio de reportes
builder.Services.AddScoped<GenReports.business.Report>();

// Registrar servicio de background para limpieza automática
builder.Services.AddHostedService<TemporaryFileCacheCleanupService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
    { 
        Title = "GenReports API", 
        Version = "v1",
        Description = "API para generación de reportes usando Telerik"
    });
    
    // Habilitar anotaciones de Swagger
    c.EnableAnnotations();
    
    // Incluir comentarios XML
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// SkiaSharp graphics engine is enabled via Telerik.Drawing.Skia package for Linux compatibility
Console.WriteLine("Using Skia graphics engine for Telerik Reporting on Linux");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
