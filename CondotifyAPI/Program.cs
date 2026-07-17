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
using MediatR;
using CondotifyAPI.Domain.Models.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddSingleton<IPrivateMediaStore, PrivateMediaStore>();

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                       ?? DatabaseContext.GetDefaultConnectionString();

builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddAutoMapper(_ => { }, typeof(CondotifyProfile).Assembly);

builder.Services.AddHttpClient<IAccessControlService, AccessControlService>();

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
builder.Services.AddHostedService<ExpiredCredentialCleanupService>();
builder.Services.AddHostedService<CredentialReconciliationWorker>();
builder.Services.AddHostedService<AccessEventIngestionWorker>();
builder.Services.AddHostedService<DeviceHealthMonitoringWorker>();

builder.Services.AddSingleton<IAccessControlDriver, IntelbrasAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriverFactory, AccessControlDriverFactory>();
builder.Services.AddScoped<IAccessControlDriver, ControlIdAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, IntelbrasUHFAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, HikvisionAccessControlDriver>();

var app = builder.Build();

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

app.UseHttpsRedirection();
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
