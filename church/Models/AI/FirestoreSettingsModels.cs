using Google.Cloud.Firestore;

namespace church.Models.AI
{
    [FirestoreData]
    public class SubscriptionServiceSettingsDocument
    {
        [FirestoreProperty("serviceCode")]
        public string ServiceCode { get; set; } = "";

        [FirestoreProperty("settings")]
        public SubscriptionSettings Settings { get; set; } = new();

        [FirestoreProperty("updatedAt")]
        public string? UpdatedAt { get; set; }
    }

    [FirestoreData]
    public class SubscriptionSettings
    {
        [FirestoreProperty("amountRulesByGrade")]
        public Dictionary<string, List<SubscriptionAmountRule>> AmountRulesByGrade { get; set; } = new();

        [FirestoreProperty("calculationStarts")]
        public Dictionary<string, SubscriptionCalculationStart> CalculationStarts { get; set; } = new();

        [FirestoreProperty("canceledMonthsByYear")]
        public Dictionary<string, List<int>> CanceledMonthsByYear { get; set; } = new();
    }

    [FirestoreData]
    public class SubscriptionAmountRule
    {
        [FirestoreProperty("grade")]
        public int Grade { get; set; }

        [FirestoreProperty("amount")]
        public decimal Amount { get; set; }

        [FirestoreProperty("hasJob")]
        public bool? HasJob { get; set; }

        [FirestoreProperty("startMonth")]
        public string StartMonth { get; set; } = "";

        [FirestoreProperty("endMonth")]
        public string? EndMonth { get; set; }
    }

    [FirestoreData]
    public class SubscriptionCalculationStart
    {
        [FirestoreProperty("year")]
        public int Year { get; set; }

        [FirestoreProperty("month")]
        public int Month { get; set; }
    }

    [FirestoreData]
    public class AttendanceServiceSettingsDocument
    {
        [FirestoreProperty("serviceCode")]
        public string ServiceCode { get; set; } = "";

        [FirestoreProperty("settings")]
        public AttendanceSettings Settings { get; set; } = new();

        [FirestoreProperty("updatedAt")]
        public string? UpdatedAt { get; set; }
    }

    [FirestoreData]
    public class AttendanceSettings
    {
        [FirestoreProperty("calculationStartDate")]
        public string CalculationStartDate { get; set; } = "";
    }
}
