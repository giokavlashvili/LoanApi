using Application.Authenticate.Services;
using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Otp.Services;
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
            // AutoMapper 16 dropped the AddAutoMapper(params Assembly[]) overload; the assembly
            // scan is now expressed through the configuration expression. Still a scan — the
            // IMapFrom<T> DTOs in this assembly need no manual registration.
            services.AddAutoMapper(cfg => cfg.AddMaps(Assembly.GetExecutingAssembly()));

            if (configuration is not null)
            {
                services.Configure<PaginationOptions>(configuration.GetSection(PaginationOptions.SectionName));

                services.AddOptions<OtpOptions>()
                    .Bind(configuration.GetSection(OtpOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<JwtOptions>()
                    .Bind(configuration.GetSection(JwtOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<RefreshTokenOptions>()
                    .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            }

            // Policy, so it lives here. Its two mechanism dependencies — IOtpCodeHasher and
            // ISmsSender — are registered in AddInfrastructureServices, which is the only reason
            // this line resolves at all: Application declares the contracts, Infrastructure
            // supplies the implementations.
            services.AddScoped<IOtpService, OtpService>();

            // Policy for the same reason: it orchestrates a repository, a unit of work, a hasher
            // and options, all through abstractions. IRefreshTokenHasher is the mechanism half and
            // is registered in AddInfrastructureServices.
            services.AddScoped<IRefreshTokenService, RefreshTokenService>();

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