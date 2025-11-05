using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MOE.Common.Models;

namespace MOE.Common.Business
{
    public class MOEService
    {
        private readonly MoeSettings _settings;
        public int StorageLocation => _settings.StorageLocation;
        public string ConnectionString => _settings.ConnectionString;

        public MOEService(MoeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (settings.StorageLocation == -1 ||
                string.IsNullOrWhiteSpace(_settings.ConnectionString))
            {
                throw new InvalidOperationException(
                    "MoeSettings are missing. Ensure the host provides StorageLocation and ConnectionString.");
            }
        }
        public MOEService(int storageLocation, string connString)
        {
            _settings = new MoeSettings
            {
                StorageLocation = storageLocation,
                ConnectionString = connString
            };
            if (StorageLocation == -1 ||
                string.IsNullOrWhiteSpace(_settings.ConnectionString))
            {
                throw new InvalidOperationException(
                    "MoeSettings are missing. Ensure the host provides StorageLocation and ConnectionString.");
            }
        }
    }
}
