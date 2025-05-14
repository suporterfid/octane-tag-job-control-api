// Program.cs
using OctaneTagJobControlAPI.Extensions;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Services.Storage;
using Microsoft.OpenApi.Models;
using OctaneTagWritingTest.Helpers;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RFID Job Control API",
        Version = "v1",
        Description = "API for controlling RFID job operations"
    });

    // Set the comments path for the Swagger JSON and UI
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

// Configure persistence services
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
builder.Services.AddPersistenceServices(dataDirectory);

// Configure application services
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<JobConfigurationService>();
builder.Services.AddHostedService<JobBackgroundService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Initialize singleton instances that the original app uses
builder.Services.AddSingleton(provider => EpcListManager.Instance);
builder.Services.AddSingleton(provider => TagOpController.Instance);

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Initialize configuration service on startup
var configService = app.Services.GetRequiredService<JobConfigurationService>();
await configService.InitializeDefaultConfigAsync();

app.Run();