// Program.cs
using OctaneTagJobControlAPI.Extensions;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Services.Storage;
using Microsoft.OpenApi.Models;
using OctaneTagWritingTest.Helpers;
using System.Text.Json.Serialization;
using OctaneTagJobControlAPI.Repositories;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers(options => 
{
    options.ModelValidatorProviders.Clear();
    options.ModelMetadataDetailsProviders.Clear();
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((x, y) => "");
    options.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(x => "");
    options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(() => "");
    options.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(() => "");
    options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(x => "");
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
})
.AddMvcOptions(options => 
{
    options.EnableEndpointRouting = false;
    options.ModelValidatorProviders.Clear();
})
.ConfigureApiBehaviorOptions(options =>
{
    options.SuppressModelStateInvalidFilter = true;
    options.SuppressInferBindingSourcesForParameters = true;
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = context => new OkResult();
    options.SuppressModelStateInvalidFilter = true;
});

// Disable model validation globally
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
    options.SuppressInferBindingSourcesForParameters = true;
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = context => new OkResult();
});

// Remove all model validation providers
builder.Services.RemoveAll<IModelValidatorProvider>();

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

// Add logging for strategy classes
builder.Services.AddStrategyLogging();

// Configure application services
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IConfigurationRepository, ConfigurationRepository>();

// Register StrategyFactory (make sure to use the correct namespace)
builder.Services.AddSingleton<OctaneTagJobControlAPI.Strategies.StrategyFactory>();

// Register JobManager after StrategyFactory
builder.Services.AddSingleton<JobManager>();

// Register JobConfigurationService
builder.Services.AddSingleton<JobConfigurationService>();

// Register background service
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

// Configure Serilog request logging
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();
app.UseFileServer(true);

// Add routes for strategy-specific SPAs
app.MapWhen(
    context => context.Request.Path.StartsWithSegments("/multi"),
    appBuilder => {
        appBuilder.UseStaticFiles();
        appBuilder.UseRouting();
        appBuilder.UseEndpoints(endpoints => {
            endpoints.MapFallbackToFile("/multi/index.html");
        });
    }
);

// Fallback to main portal for root path
app.MapFallbackToFile("/index.html");

// Initialize configuration service on startup
var configService = app.Services.GetRequiredService<JobConfigurationService>();
await configService.InitializeDefaultConfigAsync();

try
{
    Log.Information("Starting RFID Job Control API");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RFID Job Control API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}