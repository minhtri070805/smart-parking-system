using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.Services;
using ParkingOperations.Infrastructure.Clients;
using ParkingOperations.Infrastructure.Health;
using ParkingOperations.Infrastructure.Resilience;
using ParkingOperations.Infrastructure.Services;
using ParkingOperations.WebAPI.Health;

var builder = WebApplication.CreateBuilder(args);

// 1. Controller & OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 2. Kestrel & Form Limits for Image Upload (10MB)
const long maxUploadSizeBytes = 10 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadSizeBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadSizeBytes + 64 * 1024);

// 3. JWT Security & Startup Validation
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? builder.Configuration["Jwt__SecretKey"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SmartParkingApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SmartParkingClient";

if (builder.Environment.IsProduction())
{
    if (string.IsNullOrWhiteSpace(jwtSecretKey) || Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
    {
        throw new InvalidOperationException("FATAL: Jwt:SecretKey must be configured in Production with at least 32 bytes (256 bits).");
    }
}
else
{
    // In Development, if not configured, generate a transient cryptographic key
    if (string.IsNullOrWhiteSpace(jwtSecretKey) || Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
    {
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        jwtSecretKey = Convert.ToBase64String(randomBytes);
    }
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name
    };
});

builder.Services.AddAuthorization();

// 4. Downstream Recognition Client Configuration with Resilience (Polly Retry, Circuit Breaker, Timeout)
var recognitionUrl = builder.Configuration["Recognition:BaseUrl"] 
                     ?? builder.Configuration["RecognitionService:BaseUrl"] 
                     ?? "http://localhost:5002/";
var recognitionKey = builder.Configuration["Recognition:InternalKey"] 
                     ?? builder.Configuration["Recognition__InternalKey"]
                     ?? builder.Configuration["RecognitionService:InternalKey"];

builder.Services.AddHttpClient<IRecognitionClient, RecognitionHttpClient>(client =>
{
    client.BaseAddress = new Uri(recognitionUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrWhiteSpace(recognitionKey))
    {
        client.DefaultRequestHeaders.Add(RecognitionHttpClient.KeyHeader, recognitionKey);
    }
})
.AddDownstreamResilience();

// 5. Core API Client Configuration with Resilience
var coreUrl = builder.Configuration["CoreService:BaseUrl"] ?? "http://localhost:5000/";
builder.Services.AddHttpClient<ICoreHttpClient, CoreHttpClient>(client =>
{
    client.BaseAddress = new Uri(coreUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddDownstreamResilience();

// HTTP Client riêng dùng cho Health probe với timeout ngắn
builder.Services.AddHttpClient("RecognitionHealthProbe");

// 6. Health Checks & Readiness Registration
builder.Services.AddHealthChecks()
    .AddCheck<RecognitionHealthCheck>("recognition-service", tags: ["ready"]);

// 7. Application Services DI
builder.Services.AddSingleton<IFileValidationService, FileValidationService>();
builder.Services.AddScoped<IRecognitionFacade, RecognitionFacade>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoints: /health (liveness) và /readiness (readiness)
app.MapOperationsHealthChecks();

app.MapControllers();

app.Run();

public partial class Program { }
