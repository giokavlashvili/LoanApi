
using NLog.Web;
using NLog;

namespace WebApi.Middlwares.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseApplicationExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UnhandledExceptionHandlerMiddlware>();
        }

        public static IApplicationBuilder UseApplicationLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LoggingMiddleware>();
        }

        public static IApplicationBuilder UseSysLanguageMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SysLanguageMiddleware>();
        }

        public static void AddNlog(this WebApplicationBuilder builder)
        {
            //set nlog connection string
            GlobalDiagnosticsContext.Set("ConnectionString", builder.Configuration.GetConnectionString("DefaultConnection"));

            builder.Logging.ClearProviders();

            // IncludeScopes is what carries the request/response bodies from ILogger.BeginScope
            // into ${scopeproperty:...} in nlog.config. Set explicitly rather than relying on
            // the provider default, because silently losing it would empty two columns.
            builder.Host.UseNLog(new NLogAspNetCoreOptions
            {
                IncludeScopes = true,
                CaptureMessageProperties = true
            });
        }
    }
}
