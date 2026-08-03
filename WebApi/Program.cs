using Application.Common.Operations;
using Application.Extensions;
using Infrastructure.Common.Extensions;
using Infrastructure.Persistence;
using Scalar.AspNetCore;
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

    // Resolved eagerly, and only for its side effect. The registry is a singleton built by a
    // factory that validates every [VerifiableOperation] in the assembly -- duplicate names, a
    // command gated by both mechanisms, a payload marked [SensitiveData] with no key to encrypt it
    // or no redaction rule to mask it. All of those throw, which is the point: they are
    // misconfigurations that are otherwise silent, or visible only in a breach.
    //
    // Without this line the factory does not run until something first asks for the registry --
    // in practice the first request to VerificationController -- so a deployment with a broken
    // allowlist would start clean and look healthy until a user tripped over it. Failing here
    // means it never leaves the pipeline.
    app.Services.GetRequiredService<IVerifiableOperationRegistry>();

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

        // Dev-only: the OpenAPI document and both viewers describe every route, parameter and
        // schema in the API, which is reconnaissance value in Production for no corresponding
        // benefit there -- nothing in this template consumes the document at runtime.
        //
        // NSwag generates and serves the document at /swagger/v1/swagger.json; the UI below is
        // Swashbuckle's, reading that same document. Two packages, one document -- worth knowing
        // before changing either.
        app.UseOpenApi();
        app.UseSwaggerUI();

        // Scalar, alongside Swagger UI rather than replacing it. Same document, two readers: Swagger
        // UI is what everyone already has bookmarked, and Scalar renders the per-operation
        // request-body examples on Verification/Initiate far more usably -- which is the endpoint an
        // API consumer is most likely to get wrong.
        //
        // The route pattern is not optional. Scalar defaults to Microsoft.AspNetCore.OpenApi's
        // /openapi/{documentName}.json, and nothing serves that here.
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("Open Api")
                .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
                .AddDocument("v1");
        });
    }
    else
    {
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    app.UseStaticFiles();

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