using Application.Authenticate.Services;
using Application.Common.Behaviors;
using Application.Common.Interfaces;
using Application.Common.Logging;
using Application.Common.Models;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Common.Serialization;
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
            // Defaults even when no IConfiguration is supplied (tests that build a partial
            // container). Configure below overlays when a section is present.
            services.AddOptions<PerformanceLoggingOptions>();

            if (configuration is not null)
            {
                services.Configure<PaginationOptions>(configuration.GetSection(PaginationOptions.SectionName));

                services.Configure<PerformanceLoggingOptions>(
                    configuration.GetSection(PerformanceLoggingOptions.SectionName));

                services.AddOptions<OtpOptions>()
                    .Bind(configuration.GetSection(OtpOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                // No ValidateDataAnnotations: the secret is only required once an operation
                // actually carries [SensitiveData], and the registry below enforces that pairing.
                services.AddOptions<PayloadProtectionOptions>()
                    .Bind(configuration.GetSection(PayloadProtectionOptions.SectionName));

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
            // Singleton, and the options instance it holds is built once -- System.Text.Json caches
            // contract metadata per options instance, so a per-request one would rebuild every
            // contract on every call.
            services.AddSingleton<IProtectedPayloadSerializer, ProtectedPayloadSerializer>();

            services.AddSingleton<IVerifiableOperationRegistry>(provider =>
            {
                // Both arguments exist to make two silent failures loud at startup: a sensitive
                // payload with no key to encrypt it, and one encrypted at rest but still reaching
                // the Logs table in the clear. See ValidateSensitiveProperties.
                var registry = VerifiableOperationRegistry.Build(
                    Assembly.GetExecutingAssembly(),
                    provider.GetRequiredService<IPayloadProtector>().IsConfigured,
                    RedactedPropertyNames(configuration));

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

        /// <summary>
        /// The names <c>LogRedactor</c> will actually mask: defaults plus any extras from
        /// configuration. Config is additive — adding one name cannot unmask another.
        /// </summary>
        private static IReadOnlyCollection<string> RedactedPropertyNames(IConfiguration? configuration)
        {
            var configured = configuration?
                .GetSection($"{RequestLoggingSectionName}:SensitiveProperties")
                .Get<string[]>();

            return LogRedactor.MergeSensitiveProperties(configured);
        }

        /// <summary>
        /// The options class itself lives in WebApi, and Application cannot reference it — the
        /// dependency runs the other way. Only the section name is needed, so it is repeated here
        /// rather than inverted into an abstraction for one string.
        /// </summary>
        private const string RequestLoggingSectionName = "RequestLogging";
    }
}