using System;
using System.Collections.Generic;
using MOE.Common.Business;
using System.Linq;

namespace MOE.Common.Models.Repositories
{
    public interface ISignalsRepository
    {
        List<ATSPM_Signals> GetAllSignals();
        string GetSignalDescription(string signalId);
        List<ATSPM_Signals> GetAllEnabledSignals();
        List<ATSPM_Signals> EagerLoadAllSignals();
        ATSPM_Signals GetLatestVersionOfSignalBySignalID(string signalID);
        SignalFTPInfo GetSignalFTPInfoByID(string signalID);
        List<SignalFTPInfo> GetSignalFTPInfoForAllFTPSignals();
        void AddOrUpdate(ATSPM_Signals atspmSignals);
        //List<Pin> GetPinInfo();
        string GetSignalLocation(string signalID);
        void AddList(List<ATSPM_Signals> signals);
        ATSPM_Signals CopySignalToNewVersion(ATSPM_Signals originalVersion, bool isImport, string user = "");
        List<ATSPM_Signals> GetAllVersionsOfSignalBySignalID(string signalID);
        List<ATSPM_Signals> GetLatestVersionOfAllSignals();
        IQueryable<ATSPM_Signals> GetLatestVersionOfAllSignalsAsQueryable();
        List<ATSPM_Signals> GetLatestVersionOfAllSignalsForFtp();
        List<ATSPM_Signals> GetLatestVersionOfAllSignalsForSftp();
        List<ATSPM_Signals> GetLatestVersionOfAllSignalsForSftp(int controllerTypeId);
        int CheckVersionWithFirstDate(string signalId);
        List<ATSPM_Signals> GetLatestVerionOfAllSignalsByControllerType(int controllerTypeId);
        ATSPM_Signals GetVersionOfSignalByDate(string signalId, DateTime startDate);
        ATSPM_Signals GetSignalVersionByVersionId(int versionId);
        void SetVersionToDeleted(int versionId);
        void SetAllVersionsOfASignalToDeleted(string id);
        List<ATSPM_Signals> GetSignalsBetweenDates(string signalId, DateTime startDate, DateTime endDate);
        bool Exists(string signalId);
        ATSPM_Signals GetVersionOfSignalByDateWithDetectionTypes(string signalId, DateTime startDate);
    }
}