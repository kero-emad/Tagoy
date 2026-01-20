using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace church.Models
{
    public class Attendance
    {
        [Key]
        public int Id { get; set; }
        [Column(TypeName = "date")]
        public DateTime Date { get; set; }

        public Status Status {  get; set; }

        public string? Comment { get; set; }

        public DateTime? LastUpdated { get; set; }

        [ForeignKey("Students")]
        public int studentID {  get; set; }
        [ForeignKey("Users")]
        public int userID {  get; set; }
        public Students Students { get; set; }
        public Users Users { get; set; }

    }
}

public enum Status
{
    Absent,
    Present,
    Excused 
}

