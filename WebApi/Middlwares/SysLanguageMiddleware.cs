using System.Globalization;
using WebApi.Extensions;

namespace WebApi.Middlwares
{
    public class SysLanguageMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;

        public SysLanguageMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public Task Invoke(HttpContext httpContext)
        {
            string defaultLanguage = _config["DefaultLanguage"] ?? Constants.SystemCultureNames.English;
            CultureInfo culture = new CultureInfo(defaultLanguage);
            if (httpContext.Request.GetSysLanguage() != null)
                #pragma warning disable CS8604 // Possible null reference argument.
                culture = new CultureInfo(httpContext.Request.GetSysLanguage());
                #pragma warning restore CS8604 // Possible null reference argument.

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            return _next(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class SysLanguageMiddlewareExtensions
    {
        public static IApplicationBuilder UseSysLanguageMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SysLanguageMiddleware>();
        }
    }
}
