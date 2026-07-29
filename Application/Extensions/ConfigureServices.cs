using Application.Common.Behaviors;
using Application.Common.Models;
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
            services.AddAutoMapper(Assembly.GetExecutingAssembly());

            if (configuration is not null)
            {
                services.Configure<PaginationOptions>(configuration.GetSection(PaginationOptions.SectionName));
                services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
            }

            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
                // Innermost: a command that fails validation must never cost an SMS, and a slow
                // provider should show up in the performance log like any other slow handler.
                cfg.AddOpenBehavior(typeof(OtpVerificationBehavior<,>));
            });

            return services;
        }
    }
}