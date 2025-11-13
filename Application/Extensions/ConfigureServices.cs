using Application.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Application.Extensions
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddApplicationServices<Type>(this IServiceCollection services, IConfiguration? configuration = null)
        {
            //Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddAutoMapper();
            services.AddAutoMapper(cfg => 
            { 
                cfg.AddMaps(Assembly.GetExecutingAssembly());
                
                // AutoMapper 15.0.0+ requires a license key
                // Get your license key from: https://automapper.io
                // For development, you can use a trial license or purchase a license
                var licenseKey = configuration?.GetValue<string>("AutoMapper:LicenseKey");
                if (!string.IsNullOrEmpty(licenseKey))
                {
                    cfg.LicenseKey = licenseKey;
                }
            });
            
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
            services.AddMediatR(cfg => 
            {
                cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            });

            return services;
        }
    }
}