
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
    }
}
