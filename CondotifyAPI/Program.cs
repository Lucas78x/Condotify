using CondotifyAPI;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Infrastructure.Mapping;
using CondotifyAPI.Infrastructure.Repositories;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.CFTV;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Factorys;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Services.Imports;
using CondotifyAPI.Services.RecycleBin;
using CondotifyAPI.Services.Backups;
using CondotifyAPI.Services.Observability;
using CondotifyAPI.Services.Mobile;
using MediatR;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Domain.Models.Resident;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .MinimumLevel.Verbose()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            restrictedToMinimumLevel: LogEventLevel.Information
        )
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Error)
            .WriteTo.File("logs/Error/log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 5));
});

var secret = Environment.GetEnvironmentVariable("JWTCondotify_Secret")
             ?? builder.Configuration["JWT:Secret"];

if (string.IsNullOrWhiteSpace(secret))
    throw new InvalidOperationException("JWTCondotify_Secret nao definido!");

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")))
    Environment.SetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET", builder.Configuration["EquipmentEncryption:Secret"] ?? secret);

var issuer = Environment.GetEnvironmentVariable("JWTCondotify_Issuer")
             ?? builder.Configuration["JWT:Issuer"]
             ?? "Condotify";
var audience = Environment.GetEnvironmentVariable("JWTCondotify_Audience")
               ?? builder.Configuration["JWT:Audience"]
               ?? "Condotify";
var key = Encoding.UTF8.GetBytes(secret);

builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<UserAccess>, PasswordHasher<UserAccess>>();
builder.Services.AddScoped<IPasswordHasher<ResidentAccess>, PasswordHasher<ResidentAccess>>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddSingleton<FcmAccessTokenProvider>();
builder.Services.AddSingleton<IPushTransport, FcmPushTransport>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IPlatformPushNotifier, PlatformPushNotifier>();
builder.Services.AddSingleton<IPrivateMediaStore, PrivateMediaStore>();
builder.Services.AddSingleton<StructureImportCsvParser>();
builder.Services.AddScoped<IRecycleBinService, RecycleBinService>();
builder.Services.AddScoped<IConfigurationBackupService, ConfigurationBackupService>();
builder.Services.AddSingleton<IConfigurationBackupArchiveService, ConfigurationBackupArchiveService>();
builder.Services.AddScoped<IAutomaticBackupRunner, AutomaticBackupRunner>();
builder.Services.AddScoped<IOperationalAlertEvaluationService, OperationalAlertEvaluationService>();
builder.Services.AddScoped<IAlertNotificationChannelSender, AlertNotificationChannelSender>();
builder.Services.AddSingleton<ICftvStreamPathResolver, CftvStreamPathResolver>();
builder.Services.AddSingleton<IMediaAccessTokenService, MediaAccessTokenService>();
builder.Services.AddScoped<ICftvSnapshotService, CftvSnapshotService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IResidentPasswordRecoveryService, ResidentPasswordRecoveryService>();
builder.Services.AddScoped<IResidentPasswordRecoveryMailer, ResidentPasswordRecoveryMailer>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

// Ponto central: todo [Authorize] sem parametros ja existente na base passa a exigir
// principal_type=user (equipe) sem que nenhuma rota seja editada. Um token de morador
// tem principal_type=resident e portanto falha a politica padrao - a direcao do erro e
// segura, pois esquecer de anotar uma rota como acessivel ao morador a torna inacessivel,
// nunca o contrario.
// Task 7: session routes (refresh/logout/logout-all/sessions) must work for BOTH
// principal types - a bare [Authorize] would use the default policy above and lock
// residents out of logging themselves out. AnyPrincipalPolicy requires only that the
// caller is authenticated (no principal_type claim check at all), so it is used
// explicitly by name (see SessionController.AnyPrincipalPolicy) rather than by adding a
// third implicit default - the default policy itself is unchanged.
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(PrincipalTypes.Claim, PrincipalTypes.User)
        .Build())
    .AddPolicy("Resident", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PrincipalTypes.Claim, PrincipalTypes.Resident))
    .AddPolicy(CondotifyAPI.Controllers.SessionController.AnyPrincipalPolicy, policy => policy
        .RequireAuthenticatedUser());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(5),
            SegmentsPerWindow = 5,
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// A variavel de ambiente vem primeiro de proposito: ela e o mecanismo de
// deploy (docker-compose.yml a define), enquanto appsettings.json carrega um
// padrao de desenvolvimento fixo. Na ordem inversa o padrao "Server=localhost"
// sempre vencia, e a API em contentor nunca alcancava o banco.
var connectionString = Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? DatabaseContext.GetDefaultConnectionString();

builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddAutoMapper(_ => { }, typeof(CondotifyProfile).Assembly);

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("AlertNotifications", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Condotify-Notifications/1.0");
});
builder.Services.AddHttpClient("FcmPush", client =>
{
    client.BaseAddress = new Uri("https://fcm.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("FcmOAuth", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<IMediaGatewayClient, MediaGatewayClient>(client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_GATEWAY_URL") ?? "http://mediamtx:9997");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddTransient<Mediator>();
builder.Services.AddScoped<ISender, ScopedSender<Mediator>>();
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(MediatorApplicationHandlerExtension).GetTypeInfo().Assembly)
);

builder.Services.AddScoped<ICondotifyCommandsRepository, CondotifyCommandsRepository>();
builder.Services.AddScoped<ICondotifyQueriesRepository, CondotifyQueriesRepository>();
builder.Services.AddScoped<ICFTVService, CFTVService>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddScoped<IAccessRouteResolver, AccessRouteResolver>();
builder.Services.AddScoped<ICredentialReconciliationService, CredentialReconciliationService>();
builder.Services.AddScoped<IDeviceInventoryService, DeviceInventoryService>();
builder.Services.AddScoped<ILicenseAuthorizationService, LicenseAuthorizationService>();
builder.Services.AddScoped<IResidentAuthorizationService, ResidentAuthorizationService>();
builder.Services.AddHostedService<ExpiredCredentialCleanupService>();
builder.Services.AddHostedService<CredentialReconciliationWorker>();
builder.Services.AddHostedService<AccessEventIngestionWorker>();
builder.Services.AddHostedService<DeviceHealthMonitoringWorker>();
builder.Services.AddHostedService<CftvHealthMonitoringWorker>();
builder.Services.AddHostedService<CftvPathReaperWorker>();
builder.Services.AddHostedService<RecycleBinCleanupService>();
builder.Services.AddHostedService<AutomaticBackupWorker>();
builder.Services.AddHostedService<OperationalAlertWorker>();
builder.Services.AddHostedService<AlertNotificationWorker>();
builder.Services.AddHostedService<PushNotificationWorker>();

builder.Services.AddSingleton<IAccessControlDriver, IntelbrasAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriverFactory, AccessControlDriverFactory>();
builder.Services.AddScoped<IAccessControlDriver, ControlIdAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, IntelbrasUHFAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, HikvisionAccessControlDriver>();

var app = builder.Build();

var internalPort = int.TryParse(Environment.GetEnvironmentVariable("CONDOTIFY_INTERNAL_PORT"), out var configuredInternalPort)
    ? configuredInternalPort
    : 8081;

app.Use(async (context, next) =>
{
    var localPort = context.Connection.LocalPort;
    if (!InternalRouteGuard.IsAllowed(context.Request.Path.Value ?? string.Empty, localPort, internalPort))
    {
        // 404 e nao 403: nao confirmamos sequer que a rota existe.
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value ?? "anonymous");
    };
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    db.Database.Migrate();

    var legacyAccessDevices = await db.Devices
        .FromSqlRaw("""SELECT * FROM "AccessControlDevices" WHERE "Password" NOT LIKE 'enc:v1:%'""")
        .ToListAsync();
    var legacyCftvDevices = await db.CFTVDevices
        .FromSqlRaw("""SELECT * FROM "CFTVDevices" WHERE "Password" NOT LIKE 'enc:v1:%'""")
        .ToListAsync();
    foreach (var device in legacyAccessDevices)
        db.Entry(device).Property(x => x.Password).IsModified = true;
    foreach (var device in legacyCftvDevices)
        db.Entry(device).Property(x => x.Password).IsModified = true;
    if (legacyAccessDevices.Count + legacyCftvDevices.Count > 0)
        await db.SaveChangesAsync();

    if (app.Environment.IsDevelopment())
        await DevelopmentDataSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseWhen(
        context => context.Connection.LocalPort != internalPort,
        publicPipeline => publicPipeline.UseHttpsRedirection());
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "CondotifyAPI" })).AllowAnonymous();
app.MapGet("/health/ready", async (DatabaseContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready", database = "connected" })
        : Results.Json(new { status = "unavailable", database = "disconnected" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();
app.Run();
