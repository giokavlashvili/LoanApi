using Application.Common.Interfaces;
using FluentValidation.AspNetCore;
using Microsoft.OpenApi.Models;
using NLog.Web;
using NLog;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using WebUI.Filters;
using WebUI.Services;

namespace LoanApi
{
    public static class ConfigureServices
    {
        public static void AddNlog(this WebApplicationBuilder builder)
        {
            //set nlog connection string
            GlobalDiagnosticsContext.Set("ConnectionString", builder.Configuration.GetConnectionString("DefaultConnection"));

            builder.Logging.ClearProviders();
            builder.Host.UseNLog();
        }

        public static IServiceCollection AddWebUIServices(this IServiceCollection services)
        {
            services.AddDatabaseDeveloperPageExceptionFilter();
            services.AddSingleton<ICurrentUserService, CurrentUserService>();
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
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Loan API",
                    Version = "v1",
                    Description = "Loan API Services."
                });
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Authorization header using the Bearer scheme."
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {"bearer {AccessToken}"}
                    }
                });
            });

            return services;
        }
    }
}
