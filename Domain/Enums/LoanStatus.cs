using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Domain.Enums
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LoanStatus
    {
        Sent = 0,
        InProcess,
        Accepted,
        Rejected
    }
}
