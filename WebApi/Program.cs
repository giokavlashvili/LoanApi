using Application.Extensions;
using Infrastructure.Common.Extensions;
using Infrastructure.Persistence;
using Serilog;
using WebApi.Extensions;
using WebApi.Middlwares.Extensions;
using WebApi.Services;
using WebApi.Sinks;

public class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();

            // Build Angular automatically in Development mode ONLY when running in a container
            var env = host.Services.GetRequiredService<IWebHostEnvironment>();
            if (env.IsDevelopment() && AngularBuildService.IsRunningInContainer())
            {
                // Create logger factory directly without building service provider
                using var loggerFactory = LoggerFactory.Create(loggingBuilder =>
                    loggingBuilder
                        .AddConsole()
                        .SetMinimumLevel(LogLevel.Information));

                var logger = loggerFactory.CreateLogger("AngularBuildService");
                AngularBuildService.BuildIfNeeded(env.ContentRootPath, logger);
            }

            // Initialize and seed database in Development mode
            if (env.IsDevelopment())
            {
                using (var scope = host.Services.CreateScope())
                {
                    var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitializers>();
                    await initializer.InitializeAsync();
                    await initializer.SeedAsync();
                }
            }

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext();

                // Add custom PostgreSQL sink for database logging (Information and above)
                // This must be added after ReadFrom.Configuration to ensure it uses the correct minimum level
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    // Explicitly set minimum level to Information to capture all HTTP request/response logs
                    configuration.WriteTo.PostgreSQL(
                        connectionString, 
                        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);
                }
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureServices((context, services) =>
                {
                    // Register all services
                    services.AddApplicationServices<Program>(context.Configuration);
                    services.AddInfrastructureServices(context.Configuration);
                    services.AddWebUIServices();
                })
                .Configure(app =>
                {
                    var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

                    // Configure the HTTP request pipeline
                    if (env.IsDevelopment())
                    {
                        app.UseMigrationsEndPoint();
                        app.UseCors("AllowAllCorsPolicy");
                    }
                    else
                    {
                        // The default HSTS value is 30 days
                        app.UseHsts();
                    }

                    app.UseHttpsRedirection();
                    app.UseApplicationExceptionHandler();
                    app.UseApplicationLogging();
                    app.UseStaticFiles();

                    // Register the Swagger generator and the Swagger UI middleware
                    app.UseOpenApi();
                    app.UseSwaggerUI();

                    app.UseSysLanguageMiddleware();
                    app.UseCorrelationId();
                    
                    app.UseRouting();
                    
                    app.UseAuthentication();
                    app.UseAuthorization();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapRazorPages().WithStaticAssets();
                        endpoints.MapFallbackToFile("index.html");
                    });
                });
            });
}