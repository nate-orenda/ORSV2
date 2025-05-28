using System;
using System.Linq;
using ORSV2.Models;

namespace ORSV2.Utilities
{
    public static class CurrentCheckpointHelper
    {
        public static int GetCurrentCheckpoint(GACheckpointSchedule schedule, DateTime today)
        {
            if (schedule == null) return 0;

            var checkpoints = new[]
            {
                (Cp: 1, Date: schedule.Checkpoint1Date),
                (Cp: 2, Date: schedule.Checkpoint2Date),
                (Cp: 3, Date: schedule.Checkpoint3Date),
                (Cp: 4, Date: schedule.Checkpoint4Date),
                (Cp: 5, Date: schedule.Checkpoint5Date)
            };

            if (checkpoints.All(cp => !cp.Date.HasValue)) return 0;

            for (int i = checkpoints.Length - 1; i >= 0; i--)
            {
                if (checkpoints[i].Date.HasValue && today >= checkpoints[i].Date.Value)
                    return checkpoints[i].Cp;
            }

            return 0;
        }
    }
}
