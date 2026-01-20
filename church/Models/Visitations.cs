using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace church.Models
{
    public class Visitations
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string? comment {  get; set; }


        [ForeignKey("Students")]
        public int studentID { get; set; }


        [ForeignKey("Users")]
        public int userID { get; set; }

        public Students Students { get; set; }
        public Users Users { get; set; }
    }
}
