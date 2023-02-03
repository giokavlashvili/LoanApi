using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common
{
    public class Constants
    {
        public struct TokenKeys
        {
            public const string AccessToken = "access-token";
            public const string RefreshToken = "refresh-token";
            public const string RefreshTokenExpiryTime = "refresh-token-expiry-time";
        }
    }
}
