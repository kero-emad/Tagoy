namespace church.Models.DTO
{
    public class ShowStudentDTO
    {
        public int? Id { get; set; }
        public string? QR {  get; set; }
        public string? name { get; set; }
        public string? nameEn {  get; set; }
        public int? grade { get; set; }
        public DateTime? CreatedAt { get; set; }  
        public string? phone {  get; set; }
        public string? anotherPhone { get; set; }
        public string? notes { get; set; }
        public string? address {  get; set; }
        public string? area { get; set; }
        public string? location { get; set; }
        public string? confessor {  get; set; }
        public string? excused {  get; set; }
        public string? role { get; set; }
        public string? details { get; set; }
        public int? roleId { get; set; }
        public string? image { get; set; }
        public Gender? gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
    }
}
