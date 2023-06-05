using System.Globalization;

namespace LoanApi.Extensions
{
    public static class HttpRequestExtensions
    {
        public static string? GetSysLanguage(this HttpRequest request)
        {
            request.Headers.TryGetValue("x-sys-language", out var _params);

            if (_params.Any())
            {
                try
                {
                    var param = _params.First();
                        if (param!= null)
                        _ = CultureInfo.GetCultureInfo(param.Trim());
                }
                catch (CultureNotFoundException)
                {
                    return null;
                }
                return _params.First()?.Trim();
            }

            return null;
        }
    }
}
