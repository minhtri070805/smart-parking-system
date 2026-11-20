using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Options;
using SmartParking.Infrastructure.Repositories;
using SmartParking.Infrastructure.Services;
using SmartParking.Persistence;

namespace SmartParking.Infrastructure;

public static class DependencyInjection
{
    public const string StaffDutySourceKey = "StaffDuty:Source";
    public const string StaffDutyFromCoreAssignments = "CoreAssignments";
    public const string StaffDutyFromOperationsShifts = "OperationsShifts";

    /// <summary>
    /// Client gọi các service khác (microservice): Recognition (tra cứu nhận diện lúc xe ra) và Operations (ca trực).
    /// Cấu hình sai thì dừng khởi động thay vì âm thầm cho phép.
    /// </summary>
    private static void AddInternalServiceClients(IServiceCollection services, IConfiguration configuration)
    {
        var recognitionUrl = configuration["RecognitionService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(recognitionUrl))
        {
            // Tạm thời khi chưa triển khai Recognition Service: đọc DB nhận diện (xem SharedDatabaseExitRecognitionLookup).
            services.AddScoped<IExitRecognitionLookup, SharedDatabaseExitRecognitionLookup>();
        }
        else
        {
            var recognitionKey = configuration["RecognitionService:InternalKey"];
            if (!SmartParking.Application.Common.Security.InternalRequestSignature.IsSecretUsable(recognitionKey))
                throw new InvalidOperationException("RecognitionService:InternalKey requires at least 32 bytes when RecognitionService:BaseUrl is set.");
            services.AddHttpClient<IExitRecognitionLookup, HttpExitRecognitionLookup>(client =>
            {
                client.BaseAddress = RequireHttpUri(recognitionUrl, "RecognitionService:BaseUrl");
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add(RecognitionServiceClient.KeyHeader, recognitionKey);
            });
        }

        var dutySource = configuration[StaffDutySourceKey] ?? StaffDutyFromCoreAssignments;
        if (string.Equals(dutySource, StaffDutyFromOperationsShifts, StringComparison.OrdinalIgnoreCase))
        {
            var secret = configuration["OperationsApi:SharedSecret"];
            if (!SmartParking.Application.Common.Security.InternalRequestSignature.IsSecretUsable(secret))
                throw new InvalidOperationException("OperationsApi:SharedSecret requires at least 32 bytes when StaffDuty:Source = OperationsShifts.");
            var operationsUri = RequireHttpUri(configuration["OperationsApi:BaseUrl"] ?? "http://localhost:5174/", "OperationsApi:BaseUrl");
            services.AddHttpClient(nameof(OperationsShiftDutyClient), client =>
            {
                client.BaseAddress = operationsUri;
                client.Timeout = TimeSpan.FromSeconds(3);
            });
            services.AddScoped<IStaffShiftDutyClient>(sp => new OperationsShiftDutyClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OperationsShiftDutyClient)),
                secret!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OperationsShiftDutyClient>>()));
        }
        else if (!string.Equals(dutySource, StaffDutyFromCoreAssignments, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{StaffDutySourceKey} must be '{StaffDutyFromCoreAssignments}' or '{StaffDutyFromOperationsShifts}'.");
        }
    }

    private static Uri RequireHttpUri(string value, string key)
    {
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{key} must be an HTTP or HTTPS URL.");
        return uri;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 2. Strongly Typed Options
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<AdminBootstrapOptions>(configuration.GetSection(AdminBootstrapOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<RecaptchaOptions>(configuration.GetSection(RecaptchaOptions.SectionName));
        services.Configure<CloudinaryOptions>(options =>
        {
            configuration.GetSection(CloudinaryOptions.SectionName).Bind(options);
            options.Url = configuration["CLOUDINARY_URL"] ?? options.Url;
            options.CloudName = configuration["CLOUDINARY_CLOUD_NAME"] ?? options.CloudName;
            options.ApiKey = configuration["CLOUDINARY_API_KEY"] ?? options.ApiKey;
            options.ApiSecret = configuration["CLOUDINARY_API_SECRET"] ?? options.ApiSecret;
        });

        // 3. Infrastructure & Auth Services
        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddHttpClient<IRecaptchaService, RecaptchaService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<IParkingManagementService, ParkingManagementService>();
        services.AddScoped<ParkingSlotAdministrationService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IParkingSessionService, ParkingSessionService>();
        services.AddScoped<IParkingDispatchService, ParkingDispatchService>();
        services.AddScoped<IParkingAuthorizationService, ParkingAuthorizationService>();
        services.AddScoped<IStaffLotAssignmentService, StaffLotAssignmentService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddRecognitionPersistence(configuration);
        AddInternalServiceClients(services, configuration);
        var aiServiceUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:8000/";
        if (!Uri.TryCreate(aiServiceUrl.TrimEnd('/') + "/", UriKind.Absolute, out var aiServiceUri) || aiServiceUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("AiService:BaseUrl must be an HTTP or HTTPS URL.");
        services.AddHttpClient<IStaffDetectionService, StaffDetectionService>(client =>
        {
            client.BaseAddress = aiServiceUri;
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        var relativePath = configuration["ParkingLots:DataFile"] ?? "Data/parking-lots.json";
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            fullPath = Path.GetFullPath(relativePath);
        }
        services.AddScoped<IParkingLotRepository>(_ => new ParkingLotRepository(fullPath));

        // Sprint 3 – Thức: Event Ingestion Pipeline (in-memory, sẵn sàng chuyển sang EF khi DB hoàn tất)
        services.AddSingleton<IParkingEventLogRepository, InMemoryParkingEventLogRepository>();
        services.AddSingleton<IParkingSessionRepository,  InMemoryParkingSessionRepository>();

        // Sprint 3 Day 3 – Thức: Vehicle compatibility stub (TODO: replace with EF when Phụng's branch merged)
        services.AddSingleton<IVehicleCompatibilityChecker, AlwaysCompatibleVehicleChecker>();

        // Sprint 3 Day 6 – Thức: Gemini AI Integration
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.AddHttpClient<GeminiVehicleAnalyzerService>();

        var geminiApiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            services.AddSingleton<IGeminiVehicleAnalyzer, NoOpGeminiVehicleAnalyzer>();
        }
        else
        {
            services.AddScoped<IGeminiVehicleAnalyzer, GeminiVehicleAnalyzerService>();
        }

        // Sprint 3 Day 8 – Thức: Bridge to Phung's DB/Service
        services.AddScoped<IParkingEventBridge, NoOpParkingEventBridge>();

        services.AddScoped<ParkingSceneService>();
        return services;
    }
}
