using Application.Extensions;
using Infrastructure.Common.Extensions;
using Infrastructure.Persistence;
using NLog;
using NLog.Web;
using WebApi.Extensions;
using WebApi.Middlwares.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Services.AddApplicationServices<Program>(builder.Configuration);
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddWebUIServices();

    //Add NLog
    builder.AddNlog();

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

    app.UseSysLanguageMiddleware();

    app.UseApplicationExceptionHandler();

    app.UseApplicationLogging();

    app.UseResponseCaching();

    app.UseAuthentication();

    app.UseAuthorization();

    app.MapControllers();
    app.MapRazorPages()
        .WithStaticAssets(); ; // Add this line for Identity pages

    app.Run();
}
catch(Exception ex)
{
    logger.Error(ex);
    throw;
}
