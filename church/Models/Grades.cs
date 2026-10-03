using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace church.Models
{
    public class Grades
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public ICollection<Students> Students { get; set; }
    }
}
