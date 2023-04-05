using LoanApi.Extensions;
using Microsoft.VisualBasic;
using System.Globalization;

namespace LoanApi.Middlwares
{
    public class SysLanguageMiddleware
    {
        private readonly RequestDelegate _next;

        public SysLanguageMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            CultureInfo culture = new CultureInfo(Constants.SystemCultureNames.English);
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
