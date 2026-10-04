using System.Globalization;
using church.Models.AI;

namespace church.AIServices
{
    public interface IAttendanceCalculationService
    {
        AttendanceCalculationResult ApplyCalculationWindow(
            AttendanceServiceSettingsDocument settingsDocument,
            IEnumerable<AttendanceSourceRecord> sourceRecords,
            DateTime? requestedFromDate = null,
            DateTime? requestedToDate = null,
            DateTime? today = null);
    }

    public class AttendanceCalculationService :
        IAttendanceCalculationService
    {
        public AttendanceCalculationResult ApplyCalculationWindow(
            AttendanceServiceSettingsDocument settingsDocument,
            IEnumerable<AttendanceSourceRecord> sourceRecords,
            DateTime? requestedFromDate = null,
            DateTime? requestedToDate = null,
            DateTime? today = null)
        {
            if (settingsDocument == null)
                throw new ArgumentNullException(nameof(settingsDocument));

            if (!DateTime.TryParseExact(
                    settingsDocument.Settings.CalculationStartDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var calculationStartDate))
            {
                throw new InvalidOperationException(
                    "attendanceServiceSettings.settings.calculationStartDate " +
                    "is missing or invalid. Expected YYYY-MM-DD.");
            }

            calculationStartDate = calculationStartDate.Date;

            var currentDate = (today ?? DateTime.Today).Date;
            var requestedFrom = requestedFromDate?.Date ?? calculationStartDate;

            var effectiveFrom =
                requestedFrom > calculationStartDate
                    ? requestedFrom
                    : calculationStartDate;

            var effectiveTo =
                requestedToDate?.Date ?? currentDate;

            if (effectiveTo > currentDate)
                effectiveTo = currentDate;

            var result =
                new AttendanceCalculationResult
                {
                    EffectiveFromDate = effectiveFrom,
                    EffectiveToDate = effectiveTo
                };

            if (effectiveTo < effectiveFrom)
                return result;

            result.Records =
                sourceRecords
                    .Where(x =>
                        x.Date.HasValue &&
                        x.Date.Value.Date >= effectiveFrom &&
                        x.Date.Value.Date <= effectiveTo &&
                        (x.Status == 0 || x.Status == 1))
                    .OrderBy(x => x.Date)
                    .ToList();

            return result;
        }
    }
}
