using Application.Common.Interfaces;
using Application.Common.Models;
using Domain.Repositories;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Infrastructure.Common.Extensions
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Stamps the audit columns at the save boundary. It has to be resolved from the
            // container, which is why the AddDbContext calls below use the two-argument
            // (provider, options) overload — the single-argument one cannot reach it.
            //
            // Scoped is a deliberate floor, not a requirement: both of its dependencies happen to
            // be singletons today (ICurrentUserService in AddWebUIServices, IDateTime below), so a
            // singleton would also work. Registering it scoped means it stays correct if
            // ICurrentUserService ever becomes per-request, which is the direction that type is
            // likely to move — a singleton interceptor holding a scoped dependency would be a
            // captive-dependency bug, and this way that never arises.
            services.AddScoped<AuditableEntityInterceptor>();

            // For Entity Framework. One provider per process — the model itself differs between
            // them (PostgresModelConfiguration), so this is not a per-request decision.
            var databaseProvider = configuration.GetDatabaseProvider();

            // The interceptor is attached on every branch. Attached to only one of them, auditing
            // would stop working on the others and nothing would report it.
            switch (databaseProvider)
            {
                case DatabaseProvider.SqlServer:
                    services.AddDbContext<ApplicationDbContext>((provider, options) =>
                        options.UseSqlServer(
                            configuration.GetDatabaseConnectionString(DatabaseProvider.SqlServer),
                            builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
                    break;

                case DatabaseProvider.Postgres:
                    services.AddDbContext<ApplicationDbContext>((provider, options) =>
                        options.UseNpgsql(
                            configuration.GetDatabaseConnectionString(DatabaseProvider.Postgres),
                            // Named, not inferred: the PostgreSQL migrations live in their own
                            // assembly because EF resolves migrations per DbContext type, and both
                            // providers share ApplicationDbContext. See DatabaseConfiguration.
                            builder => builder.MigrationsAssembly(DatabaseConfiguration.PostgresMigrationsAssembly))
                            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
                    break;

                case DatabaseProvider.InMemory:
                    services.AddDbContext<ApplicationDbContext>((provider, options) =>
                        options.UseInMemoryDatabase("DefaultConnection")
                            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled database provider {databaseProvider}.");
            }

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<ApplicationDbContextInitialiser>();

            // Log retention. Skipped on the in-memory provider, which cannot run the batched
            // DELETE and has nothing to purge anyway.
            services.Configure<LogRetentionOptions>(configuration.GetSection(LogRetentionOptions.SectionName));

            if (databaseProvider != DatabaseProvider.InMemory)
                services.AddHostedService<LogRetentionService>();

            // For Identity
            services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                // False, deliberately. Identity's UserValidator rejects a null email when this is
                // on, so every registration failed until an email was supplied — and deriving one
                // from the user name only moved the failure, since a handle like "eqsel3" is not
                // a valid address. This template verifies people by phone (see
                // OtpVerificationBehavior) and identifies them by PersonalNumber, so email is not
                // part of the account. Turn this back on only alongside a real Email input that
                // the validator checks for format and uniqueness *before* the OTP gate.
                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            services.AddSingleton<IDateTime, DateTimeService>();
            services.AddTransient<IUserService, IdentityService>();
            services.AddTransient<IIdentityService, IdentityService>();

            // Signing only. IdentityService decides whether to issue a token; this mints it.
            // Safe as a singleton: both its dependencies are singletons, and it reads
            // IOptionsMonitor.CurrentValue per call so a rotated secret still takes effect.
            services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

            // Two step verification, mechanism half only. Swapping in a real provider is replacing
            // the ISmsSender line below — nothing else knows which vendor delivers the message.
            // IOtpService itself is policy and is registered in AddApplicationServices.
            services.AddSingleton<IOtpCodeHasher, HmacOtpCodeHasher>();
            services.AddTransient<ISmsSender, LoggingSmsSender>();

            // Mechanism half of the refresh token flow, same split: IRefreshTokenService is policy
            // and is registered in AddApplicationServices. Singleton for the same reason as
            // JwtTokenGenerator — its only dependency is a singleton and it reads CurrentValue per
            // call, so a rotated secret takes effect without a restart.
            services.AddSingleton<IRefreshTokenHasher, HmacRefreshTokenHasher>();

            // One repository per aggregate root, injected straight into the handlers that use
            // them. Reference data (Currency, LoanType) has no repository: it is read through the
            // query handlers, which project from IApplicationDbContext.
            services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
            services.AddScoped<IOtpVerificationRepository, OtpVerificationRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // Adding Authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            // Adding Jwt Bearer
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                // Deliberately false: this is a template with no HTTPS endpoint configured for
                // local development. A real deployment must run behind HTTPS and set this true.
                options.RequireHttpsMetadata = false;

                // Bound from the same JwtOptions section IdentityService signs tokens with, so
                // the signing key and the validation key cannot drift apart.
                var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                    ?? throw new InvalidOperationException("The JWT configuration section is missing.");

                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    // Deliberately false: this template has no issuer/audience configured. A real
                    // deployment must set both and turn these on, or any token signed with the
                    // right key is accepted regardless of who issued it or who it was issued for.
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret))
                };
            });

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllCorsPolicy",
                    builder => builder.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
            });

            return services;
        }
    }
}
