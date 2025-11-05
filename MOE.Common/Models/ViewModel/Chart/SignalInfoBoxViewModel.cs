using System.Collections.Generic;
using MOE.Common.Models.Repositories;

namespace MOE.Common.Models.ViewModel.Chart
{
    public class SignalInfoBoxViewModel
    {
        public SignalInfoBoxViewModel(string signalID)
        {
            var repository =
                SignalsRepositoryFactory.Create();
            var signal = repository.GetLatestVersionOfSignalBySignalID(signalID);
            SetTitle(signal);
            SetDescription(signal);
            SetMetrics(signal);
            SignalID = signalID;
        }

        public string Title { get; set; }
        public string Description { get; set; }
        public List<MetricType> MetricTypes { get; set; }
        public string SignalID { get; set; }

        private void SetDescription(ATSPM_Signals atspmSignals)
        {
            if (atspmSignals != null && atspmSignals.PrimaryName != null &&atspmSignals.SecondaryName != null)
            {
                Description = atspmSignals.PrimaryName + " " + atspmSignals.SecondaryName;
            }
            else
            {
                Description = "Primary Name or Secondary Name is not defined!";
            }
        }

        private void SetMetrics(ATSPM_Signals atspmSignals)
        {
            MetricTypes = atspmSignals.GetAvailableMetrics();
        }

        private void SetTitle(ATSPM_Signals atspmSignals)
        {
            if (SignalID != null && atspmSignals.PrimaryName != null && atspmSignals.SecondaryName != null)
            {
                Title = atspmSignals.SignalID + " - " + atspmSignals.PrimaryName + " " + atspmSignals.SecondaryName;
            }
            else
            {
                Title = "SignalID is Null or Primary Name is null or Secondary name is null";
            } 
        }
    }
}