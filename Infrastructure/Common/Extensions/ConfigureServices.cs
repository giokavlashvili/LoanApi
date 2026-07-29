using Application.Common.Interfaces;
using Domain.Common.Interfaces;
using Domain.Repositories;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Infrastructure.Common.Extensions
{
    public static class ConfigureServices
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            // For Entity Framework
            if (configuration.GetValue<bool>("UseInMemoryDatabase"))
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase("DefaultConnection"));
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"),
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
            }

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<ApplicationDbContextInitialiser>();

            // Log retention. Skipped on the in-memory provider, which cannot run the batched
            // DELETE and has nothing to purge anyway.
            services.Configure<LogRetentionOptions>(configuration.GetSection(LogRetentionOptions.SectionName));

            if (!configuration.GetValue<bool>("UseInMemoryDatabase"))
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

            services.AddTransient<IDateTime, DateTimeService>();
            services.AddTransient<IUserService, IdentityService>();
            services.AddTransient<IIdentityService, IdentityService>();

            // Two step verification. Swapping in a real provider is replacing the ISmsSender
            // line below — nothing else knows which vendor delivers the message.
            services.AddScoped<IOtpService, OtpService>();
            services.AddSingleton<IOtpCodeHasher, HmacOtpCodeHasher>();
            services.AddTransient<ISmsSender, LoggingSmsSender>();

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
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["JWT:Secret"]))
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
