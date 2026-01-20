using System.ComponentModel.DataAnnotations;

namespace church.Models
{
    public class Churches
    {
        [Key]
        public int Id { get; set; }
        public string Code { get; set; }
        public string? churchName { get; set; }
        public ICollection<ChurchServices> ChurchServices { get; set; }
    }
}
