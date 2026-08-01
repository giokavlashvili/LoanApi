using Application.Authenticate.Services;
using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Operations.Services;
using Application.Otp.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

            // Built once and validated at startup, so a misregistered operation fails at boot
            // rather than on a live request. Everything it contains is remotely executable by
            // name, which is why Build throws rather than skipping anything it cannot verify.
            services.AddSingleton<IVerifiableOperationRegistry>(provider =>
            {
                var registry = VerifiableOperationRegistry.Build(Assembly.GetExecutingAssembly());

                // Logged so the allowlist is auditable from the boot log of any environment
                // rather than only by grepping for the attribute. A stray [VerifiableOperation]
                // on a copy-pasted class shows up here on the next deployment.
                provider.GetRequiredService<ILogger<VerifiableOperationRegistry>>().LogInformation(
                    "Registered {VerifiableOperationCount} verifiable operations: {VerifiableOperations}",
                    registry.All.Count,
                    string.Join(", ", registry.All.Select(o => o.Name).Order()));

                return registry;
            });

            // Policy, same as IOtpService: it orchestrates a registry, a repository, the OTP core
            // and a unit of work, all through abstractions.
            services.AddScoped<IVerifiableOperationService, VerifiableOperationService>();

            // One implementation of the recipient rule, shared by the gate and the generic
            // endpoints. Two copies of "where does the code go" would eventually disagree, and
            // this is the rule that stops a caller redirecting their own code.
            services.AddScoped<IOtpRecipientResolver, OtpRecipientResolver>();

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
                // After validation and performance: a command that fails validation must never
                // cost an SMS, and a slow provider should show up in the performance log like any
                // other slow handler.
                cfg.AddOpenBehavior(typeof(OtpVerificationBehavior<,>));
                // Innermost, so the transaction wraps the handler and nothing else — in
                // particular, not the OTP gate above it. IssueAsync saves the challenge and then
                // texts the code; enclosed in a transaction, the OtpRequiredException that follows
                // would roll that challenge back after the message had gone out, leaving a code
                // that can never be verified. It would also roll back the attempt counter that
                // VerifyAsync persists in a finally, which is what keeps six digits from being
                // brute-forceable.
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            });

            return services;
        }
    }
}