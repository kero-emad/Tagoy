namespace church.Models.DTO
{
    public class TakeAttendanceDTO
    {
        public List<string> PresentStudents{ get; set; }
        public DateTime date { get; set; }

        public int grade {  get; set; }
    }
}
