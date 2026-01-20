using church.Models.DTO;
using church.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace church.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DeletedStudentsController : ControllerBase
    {
        context context;
        public DeletedStudentsController(context _context)
            {
            context = _context;
            }
        [Authorize]
        [HttpGet("show/{grade}")]
        public async Task <IActionResult> showDeletedStudents(int grade)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to show deleted students");
            var churchservice = await context.ChurchServices.FirstOrDefaultAsync
                 (ch => ch.Id == user.churchServiceID);



            ///////////////فرق معينه في الخدمه
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(grade))
                {
                    return StatusCode(403, "You are not allowed to show deleted students in this grade");
                }
            }


            var DeletedStudents = await context.DeletedStudents.Where
                (s => s.churchServiceID == churchservice.Id &&
                s.GradeId == grade)
                .Select(g => new
                {
                    Id = g.Id,
                    QR = g.QR,
                    name = g.Name,
                    grade = g.GradeId,
                    phone = g.phone,
                    anotherPhone= g.anotherPhone,
                    excused=g.excused,
                    role=g.role,
                    details=g.details,
                    roleId=g.roleId,
                    address = g.address,
                    image = g.image,
                    DateOfBirth = g.DateOfBirth.HasValue ? g.DateOfBirth.Value.Date : (DateTime?)null,
                    gender = g.Gender,
                    confessor = g.confessor,
                    createdAt = g.CreatedAt,
                    deletedAt=g.DeletedAt,
                    comment=g.comment
                }).ToListAsync();
            if (!DeletedStudents.Any())
                return NotFound("Not found deleted students in this service");
            return Ok(DeletedStudents);
        }

        [Authorize]
        [HttpPost("edit")]
        public async Task<IActionResult> AddComment(EditDeletedStudentsDTO dto) 
        {
            var student = await context.DeletedStudents.FirstOrDefaultAsync(ds=> ds.QR == dto.Qr);
            if (student == null) return NotFound("student not found");
            student.comment = dto.comment;
            await context.SaveChangesAsync();
            return Ok("Added comment for deleting");
        }
        [Authorize]
        [HttpGet("restore/{Qr}")]
        public async Task<IActionResult> restoreStudent(string Qr) 
        {
            var student=await context.DeletedStudents.FirstOrDefaultAsync(ds=>ds.QR == Qr);
            if (student == null) return NotFound("student not found");
            var restoredStudent = new Students
            {
                Name = student.Name,
                NameEn = student.NameEn,
                phone = student.phone,
                anotherPhone= student.anotherPhone,
                excused=student.excused,
                role = student.role,
                details = student.details,
                roleId = student.roleId,
                Gender = student.Gender,
                address = student.address,
                notes = student.notes,
                image = student.image,
                DateOfBirth = student.DateOfBirth,
                confessor = student.confessor,
                GradeId = student.GradeId,
                churchServiceID = student.churchServiceID
            };
            await context.Students.AddAsync(restoredStudent);
            context.DeletedStudents.Remove(student);
            await context.SaveChangesAsync();
            return Ok("student restored successfully");
        }
    }
}
