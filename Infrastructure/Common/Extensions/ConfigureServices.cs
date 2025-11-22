using Application.Common.Interfaces;
using Domain.Common.Interfaces;
using Domain.Repositories;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
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

            services.AddRazorPages();

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<ApplicationDbContextInitialiser>();

            // For Identity - Change from AddIdentityCore to AddIdentity
            services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            // Configure cookie authentication options (AddIdentity already registers it)
            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Identity/Account/Login";
                options.LogoutPath = "/Identity/Account/Logout";
                options.AccessDeniedPath = "/Identity/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(int.Parse(configuration["JWT:ExpireMinutes"] ?? "180"));
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });

            services.AddTransient<IDateTime, DateTimeService>();
            services.AddTransient<IUserService, IdentityService>();
            services.AddTransient<IIdentityService, IdentityService>();

            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // Adding Authentication with both JWT Bearer and Cookie
            services.AddAuthentication(options =>
            {
                // For authentication, use the policy scheme
                options.DefaultAuthenticateScheme = "JWT_OR_COOKIE";
                // For challenges (redirects), use cookie scheme - this ensures Identity redirects work
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                // Default scheme for SignIn/SignOut operations
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
            })
            // Adding Jwt Bearer
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
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
            })
            // Policy scheme that tries both JWT and Cookie for authentication
            .AddPolicyScheme("JWT_OR_COOKIE", "JWT_OR_COOKIE", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    string? authorization = context.Request.Headers.Authorization.ToString();
                    if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        return JwtBearerDefaults.AuthenticationScheme;
                    }
                    return IdentityConstants.ApplicationScheme;
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
