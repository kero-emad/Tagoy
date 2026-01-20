using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace church.Models
{
    public class Subscriptions
    {
        [Key]
        public int Id { get; set; }
        [ForeignKey("Students")]
        public int studentID { get; set; }
        [ForeignKey("Users")]
        public int userID { get; set; }
        public Students Students { get; set; }
        public Users Users { get; set; }
        public int month {  get; set; }
        public int year { get; set; }
        
        public bool isPaid { get; set; }

        public DateTime? LastUpdated { get; set; }
    }
}
