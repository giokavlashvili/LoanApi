using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Authenticate.Dtos
{
    public class LoginDto
    {
        public string AccessToken { get; set; }
        public DateTime ValidTo { get; set; }
    }
}
