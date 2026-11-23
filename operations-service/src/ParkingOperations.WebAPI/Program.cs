using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.Services;
using ParkingOperations.Infrastructure;
using ParkingOperations.Infrastructure.Clients;
using ParkingOperations.Infrastructure.Health;
using ParkingOperations.Infrastructure.Resilience;
using ParkingOperations.Infrastructure.Services;
using ParkingOperations.WebAPI.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

const long maxUploadSizeBytes = 10 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadSizeBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadSizeBytes + 64 * 1024);

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException("Operations Service requires the Core Jwt:Issuer, Jwt:Audience, and shared Jwt:SecretKey of at least 32 bytes.");

builder.Services.AddOperationsInfrastructure(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Accept", "Idempotency-Key", "X-Correlation-Id");
}));

var recognitionUrl = builder.Configuration["Recognition:BaseUrl"] ?? "http://localhost:52468/";
var recognitionKey = builder.Configuration["Recognition:InternalKey"];
builder.Services.AddHttpClient<IRecognitionClient, RecognitionHttpClient>(client =>
{
    client.BaseAddress = new Uri(recognitionUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrWhiteSpace(recognitionKey)) client.DefaultRequestHeaders.Add(RecognitionHttpClient.KeyHeader, recognitionKey);
}).AddDownstreamResilience();

var coreUrl = builder.Configuration["CoreService:BaseUrl"] ?? builder.Configuration["CoreApi:BaseUrl"] ?? "http://localhost:5295/";
builder.Services.AddHttpClient<ICoreHttpClient, CoreHttpClient>(client =>
{
    client.BaseAddress = new Uri(coreUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddDownstreamResilience();
builder.Services.AddHttpClient("RecognitionHealthProbe");
builder.Services.AddHealthChecks().AddCheck<RecognitionHealthCheck>("recognition-service", tags: ["ready"]);
builder.Services.AddSingleton<IFileValidationService, FileValidationService>();

var redisConn = builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConn);
    builder.Services.AddSingleton<IIdempotencyCache, DistributedIdempotencyCache>();
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IIdempotencyCache, InMemoryIdempotencyCache>();
}

builder.Services.AddSingleton<IRecognitionRouteConfig, RecognitionRouteConfig>();
builder.Services.AddScoped<IRecognitionFacade, RecognitionFacade>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapOperationsHealthChecks();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
