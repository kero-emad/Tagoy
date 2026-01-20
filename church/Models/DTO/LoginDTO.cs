using System.ComponentModel.DataAnnotations;

namespace church.Models.DTO
{
    public class LoginDTO
    {
        [Required(ErrorMessage ="UserName is required")]
        public string username {  get; set; }
        [Required(ErrorMessage ="Password is required")]
        public string password { get; set; }
    }
}
