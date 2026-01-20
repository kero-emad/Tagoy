using System.ComponentModel.DataAnnotations;

namespace church.Models.DTO
{
    public class RegisterDTO
    {
        [Required(ErrorMessage ="User Name is required")]
        [MinLength(6,ErrorMessage ="User Name can't be less than 6 letters")]
        public string username {  get; set; }
        [Required(ErrorMessage ="Password is required")]
        [MinLength(8,ErrorMessage ="Password can't be less than 8 characters")]
        public string password { get; set; }
        public int ChurchId { get; set; }
        public int ServiceId { get; set; }
        public int? roleId { get; set; }
        public List<int>? AllowedGrades { get; set; } = new();

    }
}
