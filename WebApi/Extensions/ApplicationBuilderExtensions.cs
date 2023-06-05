
using NLog.Web;
using NLog;

namespace LoanApi.Middlwares.Extensions
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
            builder.Host.UseNLog();
        }
    }
}
