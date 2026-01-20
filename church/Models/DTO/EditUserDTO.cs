namespace church.Models.DTO
{
    public class EditUserDTO
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public int? roleId { get; set; }
        public List<int>? allowedGrades { get; set; }
    }
}
