using System.ComponentModel.DataAnnotations;

namespace church.Models.DTO
{
    public class AddStudentsDTO
    {

        //[Required(ErrorMessage = "Student QR is required")]
        //public string qr { get; set; }
        [Required(ErrorMessage ="Student Name is required")]
        [MinLength(6,ErrorMessage ="Student Name can't be less than 6 letters")]
        public string name {  get; set; }
        [MinLength(11,ErrorMessage ="Phone must be 11 numbers")]
        [MaxLength(11,ErrorMessage ="Phone must be 11 numbers")]
        public string? phone {  get; set; }
        public string? anotherPhone { get; set; }
        public int grade {  get; set; }
        public Gender? gender { get; set; }
        public string? address {  get; set; }
        public string? area { get; set; }
        public string? location { get; set; }
        public string? excused {  get; set; }
        public string? role { get; set; }
        public string? details { get; set; }
        public string? notes { get; set; }
        public int? roleId { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public IFormFile? image { get; set; }

        public string? confessor {  get; set; }

        /*
        public string churchName {  get; set; }
        public string ServiceName { get;set; }
        */
        //public int userid { get; set; }
    }
}
