using System.Text.Json.Serialization;

namespace church.Models.DTO
{
    public class ExcusedDateDTO
    {
        public DateTime Date { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }
    }
}
