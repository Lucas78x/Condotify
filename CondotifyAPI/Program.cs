using CondotifyAPI;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Infrastructure.Mapping;
using CondotifyAPI.Infrastructure.Repositories;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Factorys;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.Reflection;
using System.Text.Json.Serialization;

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

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<DatabaseContext>();

builder.Services.AddAutoMapper(typeof(CondotifyProfile));
builder.Services.AddHttpClient<IAccessControlService, AccessControlService>();

builder.Services.AddTransient<Mediator>();
builder.Services.AddScoped<ISender, ScopedSender<Mediator>>();
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(MediatorApplicationHandlerExtension).GetTypeInfo().Assembly)
);

builder.Services.AddScoped<ICondotifyCommandsRepository, CondotifyCommandsRepository>();

// Todos os drivers disponíveis
builder.Services.AddSingleton<IAccessControlDriver, IntelbrasAccessControlDriver>();


builder.Services.AddScoped<IAccessControlDriverFactory, AccessControlDriverFactory>();
builder.Services.AddScoped<IAccessControlDriver, ControlIdAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, IntelbrasAccessControlDriver>();
builder.Services.AddScoped<IAccessControlDriver, IntelbrasUHFAccessControlDriver>();

// Service que usa a factory
builder.Services.AddScoped<IAccessControlService, AccessControlService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
