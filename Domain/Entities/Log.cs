using Domain.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Log
    {
        public long Id { get; set; }

        public DateTime When { get; set; }

        [Required]
        public string? Message { get; set; }

        [Required]
        [StringLength(10)]
        public string? Level { get; set; }

        [Required]
        public string? Exception { get; set; }

        [Required]
        public string? Trace { get; set; }

        [Required]
        public string? Logger { get; set; }

        [StringLength(100)]
        public string? Channel { get; set; }

        [Required]
        public string? Url { get; set; }
    }
}
