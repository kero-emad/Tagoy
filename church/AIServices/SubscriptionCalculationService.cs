using System.Globalization;
using church.Models.AI;

namespace church.AIServices
{
    public interface ISubscriptionCalculationService
    {
        SubscriptionBalanceResult CalculateBalance(
            SubscriptionServiceSettingsDocument settingsDocument,
            int grade,
            bool hasJob,
            IReadOnlyCollection<SubscriptionPaymentSnapshot> paymentSnapshots,
            DateTime? requestedFromMonth = null,
            DateTime? requestedToMonth = null,
            DateTime? today = null);
    }

    public class SubscriptionCalculationService :
        ISubscriptionCalculationService
    {
        public SubscriptionBalanceResult CalculateBalance(
            SubscriptionServiceSettingsDocument settingsDocument,
            int grade,
            bool hasJob,
            IReadOnlyCollection<SubscriptionPaymentSnapshot> paymentSnapshots,
            DateTime? requestedFromMonth = null,
            DateTime? requestedToMonth = null,
            DateTime? today = null)
        {
            if (settingsDocument == null)
                throw new ArgumentNullException(nameof(settingsDocument));

            var currentDate = (today ?? DateTime.Today).Date;
            var gradeKey = grade.ToString(CultureInfo.InvariantCulture);

            if (!settingsDocument.Settings.CalculationStarts.TryGetValue(
                    gradeKey,
                    out var calculationStart))
            {
                throw new InvalidOperationException(
                    $"No calculationStarts configuration exists for grade {grade}.");
            }

            ValidateMonth(
                calculationStart.Year,
                calculationStart.Month,
                "calculationStarts");

            var configuredStart =
                new DateTime(
                    calculationStart.Year,
                    calculationStart.Month,
                    1);

            var requestedStart =
                requestedFromMonth.HasValue
                    ? FirstDayOfMonth(requestedFromMonth.Value)
                    : configuredStart;

            var fromMonth =
                requestedStart > configuredStart
                    ? requestedStart
                    : configuredStart;

            var toMonth =
                requestedToMonth.HasValue
                    ? FirstDayOfMonth(requestedToMonth.Value)
                    : FirstDayOfMonth(currentDate);

            if (toMonth > FirstDayOfMonth(currentDate))
                toMonth = FirstDayOfMonth(currentDate);

            var result =
                new SubscriptionBalanceResult
                {
                    Grade = grade,
                    HasJob = hasJob
                };

            if (toMonth < fromMonth)
                return result;

            settingsDocument.Settings.AmountRulesByGrade.TryGetValue(
                gradeKey,
                out var amountRules);

            amountRules ??= new List<SubscriptionAmountRule>();

            var payments =
                paymentSnapshots
                    .GroupBy(x => $"{x.Year:D4}-{x.Month:D2}")
                    .ToDictionary(x => x.Key, x => x.Last());

            var cursor = fromMonth;

            while (cursor <= toMonth)
            {
                var yearKey = cursor.Year.ToString(CultureInfo.InvariantCulture);

                var isCanceled =
                    settingsDocument.Settings.CanceledMonthsByYear
                        .TryGetValue(yearKey, out var canceledMonths)
                    &&
                    canceledMonths.Contains(cursor.Month);

                if (!isCanceled)
                {
                    var monthKey = $"{cursor.Year:D4}-{cursor.Month:D2}";

                    payments.TryGetValue(monthKey, out var payment);

                    var rule =
                        FindRuleForMonth(
                            amountRules,
                            cursor,
                            hasJob);

                    result.Months.Add(
                        new SubscriptionMonthCalculation
                        {
                            Year = cursor.Year,
                            Month = cursor.Month,
                            IsPaid = payment?.IsPaid ?? false,
                            Amount = rule?.Amount,
                            HasPriceConfiguration = rule != null,
                            SubscriptionId = payment?.SubscriptionId,
                            UserName = payment?.UserName,
                            LastUpdated = payment?.LastUpdated
                        });
                }

                cursor = cursor.AddMonths(1);
            }

            return result;
        }

        private static SubscriptionAmountRule?
            FindRuleForMonth(
                IReadOnlyCollection<SubscriptionAmountRule> rules,
                DateTime targetMonth,
                bool hasJob)
        {
            var applicable =
                rules
                    .Where(rule => IsRuleActive(rule, targetMonth))
                    .ToList();

            var exact =
                applicable
                    .Where(x =>
                        x.HasJob.HasValue &&
                        x.HasJob.Value == hasJob)
                    .OrderByDescending(x => ParseYearMonth(x.StartMonth))
                    .FirstOrDefault();

            if (exact != null)
                return exact;

            return applicable
                .Where(x => !x.HasJob.HasValue)
                .OrderByDescending(x => ParseYearMonth(x.StartMonth))
                .FirstOrDefault();
        }

        private static bool IsRuleActive(
            SubscriptionAmountRule rule,
            DateTime targetMonth)
        {
            if (!TryParseYearMonth(rule.StartMonth, out var startMonth))
                return false;

            if (targetMonth < startMonth)
                return false;

            if (string.IsNullOrWhiteSpace(rule.EndMonth))
                return true;

            if (!TryParseYearMonth(rule.EndMonth!, out var endMonth))
                return false;

            return targetMonth <= endMonth;
        }

        private static DateTime FirstDayOfMonth(DateTime value) =>
            new(value.Year, value.Month, 1);

        private static DateTime ParseYearMonth(string value)
        {
            return TryParseYearMonth(value, out var result)
                ? result
                : DateTime.MinValue;
        }

        private static bool TryParseYearMonth(
            string value,
            out DateTime result)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);
        }

        private static void ValidateMonth(
            int year,
            int month,
            string fieldName)
        {
            if (year < 2000 ||
                year > 2100 ||
                month < 1 ||
                month > 12)
            {
                throw new InvalidOperationException(
                    $"Invalid {fieldName} date: {year:D4}-{month:D2}.");
            }
        }
    }
}
