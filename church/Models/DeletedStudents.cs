using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace church.Models
{
    public class DeletedStudents
    {
        [Key]
        public int Id { get; set; }
        public string? QR { get; set; }
        public string Name { get; set; }
        public string? NameEn { get; set; }
        public string? phone { get; set; }
        public string? anotherPhone { get; set; }
        public Gender? Gender { get; set; }
        public string? address { get; set; }
        public string? image { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? confessor { get; set; }
        public string? excused { get; set; }
        public string? role { get; set; }
        public string? details { get; set; }
        public string? notes { get; set; }
        public int? roleId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        public string? comment {  get; set; }

        [ForeignKey("ChurchServices")]
        public int churchServiceID { get; set; }

        [ForeignKey("Grades")]
        public int GradeId { get; set; }
        public Grades Grades { get; set; }
        public ChurchServices ChurchServices { get; set; }

        
    }
}
