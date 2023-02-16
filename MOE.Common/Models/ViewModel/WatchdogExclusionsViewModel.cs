using System.Collections.Generic;
using MOE.Common.Models.Repositories;

namespace MOE.Common.Models.ViewModel
{
    public class WatchdogExclusionsViewModel
    {
        public List<SPMWatchdogExclusions> Exclusions;

        public WatchdogExclusionsViewModel()
        {
            Exclusions = new List<SPMWatchdogExclusions>();
            SetList();
        }

        public void SetList()
        {
            var repository = SPMWatchdogExclusionsRepositoryFactory.Create();
            Exclusions = repository.GetSPMWatchdogExclusions();
            foreach (var exclusion in Exclusions)
            {
                exclusion.SetAlertDescription();
            }
        }
    }
}