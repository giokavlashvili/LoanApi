using Application.Common.Interfaces;
using Application.Common.Operations;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Localization;
using NSwag;
using NSwag.Generation.Processors.Security;
using System.Text.Json.Serialization;
using WebApi.Filters;
using WebApi.Localization;
using WebApi.Middlwares;
using WebApi.Routing;
using WebUI.Filters;
using WebUI.Services;

namespace WebApi.Extensions
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddWebUIServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDatabaseDeveloperPageExceptionFilter();
            services.Configure<RequestLoggingOptions>(configuration.GetSection(RequestLoggingOptions.SectionName));
            services.AddSingleton<ICurrentUserService, CurrentUserService>();
            services.AddSingleton<IStringLocalizer, JsonStringLocalizer>();
            services.AddHttpContextAccessor();

            services.AddControllers(options =>
                {
                    options.Filters.Add<ApiExceptionFilterAttribute>();

                    // Applies to the [controller] token in every route template, so resource
                    // segments are lowercase kebab-case (loan-applications) without any controller
                    // spelling its own path. See SlugifyParameterTransformer for what it does and
                    // does not touch.
                    options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));
                })
                .AddJsonOptions(x =>
                {
                    // serialize enums as strings in api responses
                    x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();

            // AddOpenApiDocument, not AddSwaggerDocument: the two differ only in the document format
            // they emit — OpenAPI 3.0 versus Swagger 2.0. OpenAPI 3 is what makes named request-body
            // examples possible, which is the only way Verification/Initiate can offer a per-operation
            // body picker in Swagger UI. Swagger 2.0 has no equivalent, and no `oneOf` either.
            //
            // The service-provider overload, because the payload processor needs the operation
            // registry — the document must describe what is actually registered, not what the enum
            // happens to define.
            services.AddOpenApiDocument((configure, serviceProvider) =>
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

                // Verified-operation payload types have no route of their own — registering an
                // operation means removing its direct endpoint — so without this they never reach
                // the document and the generated client types the payload as nothing at all.
                configure.DocumentProcessors.Add(new VerifiableOperationPayloadsDocumentProcessor(
                    serviceProvider.GetRequiredService<IVerifiableOperationRegistry>()));

                // Last, so it also covers the payload schemas injected above. Undoes the one
                // regression the OpenAPI 3 switch would otherwise carry API-wide -- see the class.
                configure.DocumentProcessors.Add(new NonNullablePropertiesAreRequiredDocumentProcessor());
            });

            return services;
        }
    }
}
