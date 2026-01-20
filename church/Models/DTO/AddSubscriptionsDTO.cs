namespace church.Models.DTO
{
    public class AddSubscriptionsDTO
    {
        public List<string> students { get; set; }
        public int? month { get; set; }

        public int? year { get; set; }

        public int grade { get; set; }
    }
}
