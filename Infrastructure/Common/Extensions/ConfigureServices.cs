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
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
                var connectionString = configuration.GetConnectionString("DefaultConnection");
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString,
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
            }

            services.AddRazorPages();

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services.AddScoped<ApplicationDbContextInitializers>();

            // For Identity - Change from AddIdentityCore to AddIdentity
            services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                // User settings
                o.User.RequireUniqueEmail = true;

                // Sign-in settings
                o.SignIn.RequireConfirmedEmail = true; // Require email confirmation before login
                o.SignIn.RequireConfirmedAccount = true; // Show confirmation page after registration

                // Two-factor settings
                o.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
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

                // For API requests, return 401 immediately instead of redirecting
                // The Angular interceptor will catch 401 and redirect to login page
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    // For non-API requests (Razor Pages), use default redirect behavior
                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    // For API requests, return 403 immediately instead of redirecting
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                    // For non-API requests, use default redirect behavior
                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

            services.AddTransient<IEmailSender, NoOpEmailSender>();

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
