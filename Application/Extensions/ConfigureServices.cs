using Application.Authenticate.Services;
using Application.Common.Behaviors;
using Application.Common.Confirmation;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Confirmation.Services;
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

                services.AddOptions<PendingOperationOptions>()
                    .Bind(configuration.GetSection(PendingOperationOptions.SectionName))
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

            // Built once by scanning this assembly, and it throws rather than starting if any
            // confirmable command is declared wrongly — a duplicate discriminator, a missing
            // marker, or a secret that would be persisted into a parked payload. Failing at boot
            // is the point: the alternative is discovering it when someone tries to confirm.
            // Constructed here and registered as an instance, not behind a factory lambda: a
            // factory is resolved lazily, so a bad declaration would surface on the first request
            // that happened to need the registry rather than at startup. This runs the scan while
            // the container is still being composed, which is what "fails at boot" has to mean.
            services.AddSingleton<IOperationTypeRegistry>(new OperationTypeRegistry(Assembly.GetExecutingAssembly()));

            // Scoped, and carries the confirmed-execution flag for one request only. This is what
            // keeps the signal off the command, where a caller could set it and skip confirmation.
            services.AddScoped<IOperationExecutionContext, OperationExecutionContext>();
            services.AddScoped<IOperationConfirmationService, OperationConfirmationService>();

            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
                // Innermost: a command that fails validation must never cost an SMS, and a slow
                // provider should show up in the performance log like any other slow handler.
                cfg.AddOpenBehavior(typeof(OtpVerificationBehavior<,>));
                // The other confirmation topology, on the same reasoning. The two are mutually
                // exclusive per command — the registry refuses a type that opts into both — so
                // the relative order of these two never decides anything.
                cfg.AddOpenBehavior(typeof(OperationConfirmationBehavior<,>));
            });

            return services;
        }
    }
}