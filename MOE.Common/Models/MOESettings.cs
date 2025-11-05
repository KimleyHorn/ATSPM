using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MOE.Common.Models
{
    public sealed class MoeSettings
    {
        public int StorageLocation { get; set; } = 0;
        public string ConnectionString { get; set; } = "";
    }
}
