using MOE.Common.Business;

namespace MOE.Common.Models.Repositories
{
    public class ControllerEventLogRepositoryFactory
    {
        private static IControllerEventLogRepository controllerEventLogRepository;
        private static MOEService _settings;

        public static IControllerEventLogRepository Create(SPM db)
        {
            return new ControllerEventLogRepository(db);
        }
        public static IControllerEventLogRepository Create(MOEService settings = null)
        {
            _settings = settings;
            if (controllerEventLogRepository != null)
            {
                return controllerEventLogRepository;
            }
            else
            {
                return new ControllerEventLogRepository(_settings);
            }
        }

        public static void SetRepository(IControllerEventLogRepository newRepository)
        {
            controllerEventLogRepository = newRepository;
        }
    }
}