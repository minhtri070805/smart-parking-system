using System.Text;
using Microsoft.AspNetCore.Http.Features;
using recognition.Api.Endpoints;
using recognition.Api.Filters;
using recognition.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var internalKey = builder.Configuration["Recognition:InternalKey"] ?? Environment.GetEnvironmentVariable("RECOGNITION_INTERNAL_KEY");
if (string.IsNullOrWhiteSpace(internalKey) || Encoding.UTF8.GetByteCount(internalKey) < 32)
    throw new InvalidOperationException("Recognition:InternalKey requires at least 32 bytes.");

const long maxBytes = 10 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBytes + 64 * 1024);

builder.Services.AddRecognitionInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Recognition Service v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Recognition Service" }));

var recognitionGroup = app.MapGroup("/internal/recognition");
recognitionGroup.AddEndpointFilter(new ServiceKeyAuthFilter(internalKey));
recognitionGroup.MapRecognitionEndpoints();

app.Run();
