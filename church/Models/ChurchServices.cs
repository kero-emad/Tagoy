using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace church.Models
{
    public class ChurchServices
    {
        [Key]
        public int Id { get; set; }
        [ForeignKey("Churches")]
        public int ChurchID {  get; set; }
        [ForeignKey("Services")]
        public int ServiceID {  get; set; }
        public Churches Churches { get; set; }
        public Services Services { get; set; }

        public ICollection<Students> Students { get; set; }
        public ICollection<Users> Users { get; set; }
    }
}
