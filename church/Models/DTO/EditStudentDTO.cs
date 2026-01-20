namespace church.Models.DTO
{
    public class EditStudentDTO
    {
        public string? name { get; set; }
        public int? grade { get; set; }
        public string? phone { get; set; }
        public string? NameEn { get; set; }
        public string? anotherPhone { get; set; }
        public string? address { get; set; }
        public string? area { get; set; }
        public string? location { get; set; }
        public string? excused {  get; set; }
        public string? role { get; set; }
        public string? details { get; set; }
        public string? notes {  get; set; }
        public int? roleId { get; set; }
        public IFormFile? image { get; set; }
        public Gender? gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? confessor { get; set; }
    }
}
