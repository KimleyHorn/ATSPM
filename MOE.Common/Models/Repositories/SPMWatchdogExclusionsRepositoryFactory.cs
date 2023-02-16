namespace MOE.Common.Models.Repositories
{
    public class SPMWatchdogExclusionsRepositoryFactory
    {
        private static ISPMWatchdogExclusionsRepository exclusionsRepository;

        public static ISPMWatchdogExclusionsRepository Create()
        {
            if (exclusionsRepository != null)
                return exclusionsRepository;
            return new SPMWatchdogExclusionsRepository();
        }

        public static void SetRepository(ISPMWatchdogExclusionsRepository repository)
        {
            exclusionsRepository = repository;
        }
    }
}