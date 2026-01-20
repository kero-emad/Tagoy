using System.ComponentModel.DataAnnotations;

namespace church.Models
{
    public class Services
    {
        [Key]
        public int Id { get; set; }
        public string Code { get; set; }

        public string? serviceName {  get; set; }
        //public ICollection<Grades>Grades { get; set; }
        public ICollection<ChurchServices> ChurchServices { get; set; }
    }
}
