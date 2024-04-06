using Application.Common.Interfaces;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Localization;
using NSwag;
using NSwag.Generation.Processors.Security;
using System.Text.Json.Serialization;
using WebApi.Filters;
using WebApi.Localization;
using WebUI.Filters;
using WebUI.Services;

namespace WebApi.Extensions
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddWebUIServices(this IServiceCollection services)
        {
            services.AddDatabaseDeveloperPageExceptionFilter();
            services.AddSingleton<ICurrentUserService, CurrentUserService>();
            services.AddSingleton<IStringLocalizer, JsonStringLocalizer>();
            services.AddHttpContextAccessor();

            services.AddControllers(options => options.Filters.Add<ApiExceptionFilterAttribute>())
                .AddJsonOptions(x =>
                {
                    // serialize enums as strings in api responses
                    x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });


            services.AddFluentValidationAutoValidation();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();

            services.AddSwaggerDocument(configure =>
            {
                configure.Title = "Open Api";
                configure.AddSecurity("JWT", Enumerable.Empty<string>(), new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.ApiKey,
                    Name = "Authorization",
                    In = OpenApiSecurityApiKeyLocation.Header,
                    Description = "Type into the textbox: Bearer {your JWT token}."
                });

                configure.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("JWT"));
                configure.OperationProcessors.Add(new SysLanguageHeaderOperationProcessor());
            });

            return services;
        }
    }
}
