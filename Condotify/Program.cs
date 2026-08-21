using Condotify.Components;
using Condotify.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "data-protection");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Condotify")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

builder.Services.AddControllersWithViews(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    var allowedHost = builder.Configuration["ReverseProxy:AllowedHost"];
    if (!string.IsNullOrWhiteSpace(allowedHost)) options.AllowedHosts.Add(allowedHost);
});
builder.Services.AddSingleton<WebSessionRefreshCoordinator>();
builder.Services.AddScoped<ISessionContextProvider, ClaimsSessionContextProvider>();
builder.Services.AddScoped<CondotifyApiClient>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";          
        options.LogoutPath = "/Login/Logout";  
        options.Cookie.Name = "Condotify.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/Login/KeepAlive"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Não foi possível concluir a operação. Tente novamente.");
        });
    });
    app.UseHsts();
}

if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});
if (app.Environment.IsDevelopment())
{
    var developmentContentTypes = new FileExtensionContentTypeProvider();
    developmentContentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";
    app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = developmentContentTypes });
}
else
{
    app.UseStaticFiles();
}

app.UseRouting();

app.UseAuthentication(); 
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllerRoute(
    name: "login",
    pattern: "Login/{action=Login}/{id?}",
    defaults: new { controller = "Login" });

app.MapGet("/health/live", () => Results.Ok(new { Status = "Healthy" }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
