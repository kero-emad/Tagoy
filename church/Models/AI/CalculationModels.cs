namespace church.Models.AI
{
    public class SubscriptionPaymentSnapshot
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsPaid { get; set; }
        public int? SubscriptionId { get; set; }
        public string? UserName { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    public class SubscriptionMonthCalculation
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthKey => $"{Year:D4}-{Month:D2}";
        public bool IsPaid { get; set; }
        public decimal? Amount { get; set; }
        public bool HasPriceConfiguration { get; set; }
        public int? SubscriptionId { get; set; }
        public string? UserName { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    public class SubscriptionBalanceResult
    {
        public int Grade { get; set; }
        public bool HasJob { get; set; }
        public List<SubscriptionMonthCalculation> Months { get; set; } = new();

        public int PaidMonthCount => Months.Count(x => x.IsPaid);
        public int UnpaidMonthCount => Months.Count(x => !x.IsPaid);
        public int MissingPriceMonthCount =>
            Months.Count(x => !x.IsPaid && !x.HasPriceConfiguration);

        public decimal KnownTotalDue =>
            Months.Where(x => !x.IsPaid && x.Amount.HasValue)
                  .Sum(x => x.Amount!.Value);

        public bool HasMissingPriceConfiguration =>
            MissingPriceMonthCount > 0;
    }

    public class AttendanceSourceRecord
    {
        public DateTime? Date { get; set; }
        public int? Status { get; set; }
        public string? Comment { get; set; }
        public string? Excused { get; set; }
        public DateTime? LastUpdated { get; set; }
        public string? UserName { get; set; }
    }

    public class AttendanceCalculationResult
    {
        public DateTime EffectiveFromDate { get; set; }
        public DateTime EffectiveToDate { get; set; }
        public List<AttendanceSourceRecord> Records { get; set; } = new();

        public int PresentCount => Records.Count(x => x.Status == 1);
        public int AbsentCount => Records.Count(x => x.Status == 0);
    }
}
