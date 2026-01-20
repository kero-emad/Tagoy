
using church.Models;
using church.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;

namespace church.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttendanceController : ControllerBase
    {
        context context;
            public AttendanceController(context _context) 
            {
            context = _context;
            }
        [Authorize]
        [HttpPost("mark-attendance")]
        public async Task<IActionResult> MarkAttendanceBulk(TakeAttendanceDTO takeAttendanceDTO)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("User not found, please login.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("User not found, please login.");


            if (user.allowedGrades != null && user.allowedGrades.Any() && !user.allowedGrades.Contains(takeAttendanceDTO.grade))
                return StatusCode(403,"You are not allowed to take attendance for this grade.");

            var studentsInGrade = await context.Students
                .Where(s => s.churchServiceID == user.churchServiceID && s.GradeId == takeAttendanceDTO.grade)
                .ToListAsync();

            if (!studentsInGrade.Any())
                return NotFound("No students found in this grade and service.");

            // الطلاب اللي اتسجلوا قبل كده سواء حاضرين او غايبين
            var existingAttendance = await context.Attendance
                .Where(a => a.Date.Date == takeAttendanceDTO.date.Date)
                .Select(a => a.studentID)
                .ToListAsync();

            var attendancesToAdd = new List<Attendance>();

            foreach (var student in studentsInGrade)
            {
                if (existingAttendance.Contains(student.Id))
                    continue;///////////اتسجل قبل كده سواء حاضر او غايب في نفس اليوم

                var status = takeAttendanceDTO.PresentStudents.Contains(student.QR)
                   ? Status.Present
            :       Status.Absent;

                attendancesToAdd.Add(new Attendance
                {
                    studentID = student.Id,
                    userID = user.Id,
                    Date = takeAttendanceDTO.date,
                    Status = status,
                    LastUpdated=DateTime.Now,
                    
                });
            }

            if (!attendancesToAdd.Any())
                return BadRequest("Attendance already marked for all students in this grade.");

            context.Attendance.AddRange(attendancesToAdd);
            await context.SaveChangesAsync();

            return Ok("Attendance marked successfully.");
        }

        
        [Authorize]
        [HttpPost("show-attendance")]
        public async Task<IActionResult>ShowAttendance(ShowAttendanceDTO showAttendanceDTO)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to add new students");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");

            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(showAttendanceDTO.grade))
                {
                    return StatusCode(403, "You are not allowed to view attendance for this grade.");
                }
            }

            var attendanceList =await context.Attendance
        .    Include(a => a.Students).Include(a=>a.Users)
             .Where(a =>
              a.Date.Date == showAttendanceDTO.date.Date &&
              a.Students.churchServiceID == user.churchServiceID &&
              a.Students.GradeId == showAttendanceDTO.grade 
               )
             .Select(a => new
              {
              AttendanceID=a.Id,
              a.Students.Id,
              a.Students.QR,
              a.Students.Name,
              a.Students.excused,
              a.Students.phone,
              a.Status,
              a.Comment,
              a.LastUpdated,
              a.Users.UserName
              })
             .ToListAsync();
            return Ok(attendanceList);
        }
        [Authorize]
        [HttpGet("show-attendance/{QR}")]
        public async Task<IActionResult> showAttendanceOfStudent(string QR)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to add new students");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");

            var student = await context.Students.FirstOrDefaultAsync
                (s => s.QR == QR);
            if (student == null) return NotFound("Student Not Found");

            if (user.churchServiceID != student.churchServiceID)
                return StatusCode(403, "Not allowed to show the attendance of this student");

            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                {
                    return StatusCode(403, "You are not allowed to view attendance for this grade.");
                }
            }
            var attendanceRecords = await context.Attendance
             .Where(a => a.studentID == student.Id)
             .Include(a=>a.Users)
             .Select(a => new
                 { 
              a.Date.Date,
              a.Status,
              a.Comment,
              a.LastUpdated,
              a.Users.UserName
        })
        .ToListAsync();
            return Ok (attendanceRecords);
        }


        [Authorize]
        [HttpPut("edit-attendance/{id}")]
        public async Task<IActionResult> editAttendance(int id,[FromBody]EditAttendanceDTO dto) 
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to add new students");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");

            var attendance = await context.Attendance
              .Include(a => a.Students)
              .FirstOrDefaultAsync(a => a.Id == id);

              if (attendance == null)
                 return NotFound("Attendance record not found.");

            
            if (attendance.Students.churchServiceID != user.churchServiceID)
                return StatusCode(403, "You are not allowed to edit this student's attendance.");

           
            if (user.allowedGrades != null && user.allowedGrades.Any()
                && !user.allowedGrades.Contains(attendance.Students.GradeId))
            {
                return StatusCode(403, "You are not allowed to edit this grade’s attendance.");
            }

            if (dto.Status.HasValue)
                attendance.Status = dto.Status.Value;

            if (!string.IsNullOrWhiteSpace(dto.Comment))
                attendance.Comment = dto.Comment;
            attendance.userID = int.Parse(userID);
            attendance.LastUpdated = DateTime.Now;
            await context.SaveChangesAsync();

            return Ok("Attendance updated successfully.");
        }



        [Authorize]
        [HttpPost("show-attendance-by-month")]
        public async Task<IActionResult> ShowAttendanceByMonth(ShowAttendanceByMonthDTO dto)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to view attendance");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to view attendance");

       
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(dto.grade))
                {
                    return StatusCode(403, "You are not allowed to view attendance for this grade.");
                }
            }

            
            int selectedYear = dto.year ?? DateTime.Now.Year;

            
            var rawAttendance = await context.Attendance
                .Include(a => a.Students)
                .Where(a =>
                    a.Date.Month == dto.month &&
                    a.Date.Year == selectedYear &&
                    a.Students.churchServiceID == user.churchServiceID &&
                    a.Students.GradeId == dto.grade
                )
                .ToListAsync();


            var attendanceList = rawAttendance
                .GroupBy(a => new { a.Students.Id, a.Students.Name, a.Students.phone, a.Students.QR ,a.Students.excused})
                .Select(g => new
                {
                    Id = g.Key.Id,
                    Qr = g.Key.QR,
                    Name = g.Key.Name,
                    Phone = g.Key.phone,
                    excused=g.Key.excused,
                    Attendance = g
                        .Where(x => x.Status == Status.Present)
                        .Select(x => x.Date.ToString("yyyy-MM-dd"))
                        .Distinct()
                        .ToList(),
                    Apologies = g
                         .Where(x => x.Status == Status.Excused)
                         .Select(x => new
                         {
                             Date=x.Date.ToString("yyyy-MM-dd"),
                             comment=x.Comment
                         })
                         .Distinct()
                         .ToList()
                })
                .ToList();
                


            return Ok(attendanceList);
        }



    }
}
