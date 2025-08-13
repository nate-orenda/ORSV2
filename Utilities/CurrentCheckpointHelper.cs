using System;
using System.Linq;
using ORSV2.Models;

namespace ORSV2.Utilities
{
    public static class CurrentCheckpointHelper
    {
        public static int GetCurrentCheckpoint(GACheckpointSchedule? schedule, DateTime today)
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
                if (checkpoints[i].Date is DateTime date && today >= date)
                    return checkpoints[i].Cp;
            }

            return 0;
        }
        public static List<int> GetBuildableCheckpoints(GACheckpointSchedule schedule, DateTime today, List<int> existingProtocols)
        {
            int currentCP = GetCurrentCheckpoint(schedule, today);

            return Enumerable.Range(1, currentCP)
                .Where(cp => !existingProtocols.Contains(cp))
                .ToList();
        }

        public static List<(int CP, string Label)> GetBuildableCheckpointLabels(GACheckpointSchedule? schedule, DateTime today, List<int> existingProtocols)
        {
            if (schedule == null)
                return new List<(int, string)>();

            int currentCP = GetCurrentCheckpoint(schedule, today);

            var checkpoints = new[]
            {
                (Cp: 1, Date: schedule.Checkpoint1Date),
                (Cp: 2, Date: schedule.Checkpoint2Date),
                (Cp: 3, Date: schedule.Checkpoint3Date),
                (Cp: 4, Date: schedule.Checkpoint4Date),
                (Cp: 5, Date: schedule.Checkpoint5Date)
            };

            return checkpoints
                .Where(c => c.Cp <= currentCP && !existingProtocols.Contains(c.Cp))
                .Select(c =>
                {
                    var label = $"Checkpoint {c.Cp}" + (c.Date is DateTime date ? $" – {date:MMM d}" : "");
                    return (c.Cp, label);
                })
                .ToList();
        }

        public static int GetCurrentSchoolYear(DateTime date)
        {
            // Aug-Dec → year + 1
            // Jan-Jul → year
            if (date.Month >= 8 && date.Month <= 12)
                return date.Year + 1;

            return date.Year;
        }


    }
}
