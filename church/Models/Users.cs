using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace church.Models
{
    public class Users
    {
        [Key]
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool IsAdmin { get; set; }
        public int? roleId { get; set; }
        [ForeignKey("ChurchServices")]
        public int churchServiceID { get; set; }

        public List<int>? allowedGrades { get; set; }=new List<int>();
        public ChurchServices ChurchServices { get; set; }

        public ICollection<Attendance> Attendances { get; set; }
    }
}
