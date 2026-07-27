using Application.Extensions;
using Infrastructure.Common.Extensions;
using Infrastructure.Persistence;
using Serilog;
using WebApi.Extensions;
using WebApi.Middlwares.Extensions;

// Covers the window before the container exists — a bad connection string or a DI
// misconfiguration would otherwise fail with no log line at all. Replaced wholesale by
// the configured pipeline once AddApplicationLogging runs.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Services.AddApplicationServices<Program>(builder.Configuration);
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddWebUIServices(builder.Configuration);

    //Add Serilog
    builder.AddApplicationLogging();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
        // Initialize and seed database
        using (var scope = app.Services.CreateScope())
        {
            var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            await initialiser.InitialiseAsync();
            await initialiser.SeedAsync();
        }

        app.UseCors("AllowAllCorsPolicy");
    }
    else
    {
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    app.UseStaticFiles();

    // Register the Swagger generator and the Swagger UI middleware
    app.UseOpenApi();
    app.UseSwaggerUI();

    // Order matters. Logging is outermost so it observes the final status code and response
    // body of everything below it — including the 500 the exception handler produces, which
    // the previous order (handler outside logging) could never record. Static files, Swagger
    // and the OpenAPI document are registered above and so stay out of the log entirely.
    app.UseApplicationLogging();

    app.UseApplicationExceptionHandler();

    app.UseSysLanguageMiddleware();

    app.UseResponseCaching();

    app.UseAuthentication();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    // Drains the batch queues before exit. Matters more than NLog's Shutdown() did: the
    // database sink holds up to BatchPostingLimit events, and without this they are lost.
    Log.CloseAndFlush();
}