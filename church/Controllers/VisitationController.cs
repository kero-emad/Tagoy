using church.Models;
using church.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace church.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VisitationController : ControllerBase
    {
        context context;
        public VisitationController (context _context) 
        {
            context = _context;
        }
        [Authorize]
        [HttpPost("add")]
        public async Task<IActionResult> takeVisitation(AddVisitationDTO dto)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null)
                return Unauthorized("User not found, please login.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null)
                return Unauthorized("User not found.");

            var student = await context.Students.FirstOrDefaultAsync(s => s.QR == dto.qr);
            if (student == null)
                return NotFound("Student not found");

            //if (student.churchServiceID != user.churchServiceID)
            //    return StatusCode(403, "You are not allowed to add visitation for this student");

            var visitation = new Visitations
            {
                studentID = student.Id,
                userID = user.Id,
                Date = dto.date ?? DateTime.Now,
                comment = dto.comment
            };
            context.Visitations.Add(visitation);
            await context.SaveChangesAsync();

            return Ok("Visitation added successfully");
        }

        [Authorize]
        [HttpPost("show")]
        public async Task<IActionResult> ShowVisitations([FromBody] GetVisitationsDTO dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized("User not authenticated.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
            if (user == null) return Unauthorized("User not found.");

            //if (user.allowedGrades != null && user.allowedGrades.Any() && !user.allowedGrades.Contains(dto.GradeId))
            //   return StatusCode(403, "You are not allowed to view visitations for this grade.");

            var year = dto.year ?? DateTime.Now.Year;

            var visitations = await context.Visitations
                .Include(v => v.Students)
                .Include(v => v.Users)
                .Where(v =>
                    v.Students.GradeId == dto.grade &&
                    v.Students.churchServiceID == user.churchServiceID &&
                    v.Date.Month == dto.month &&
                    v.Date.Year == year
                )
                .Select(v => new
                {
                    v.Id,
                    studentId=v.Students.Id,
                    studentQr=v.Students.QR,
                    StudentName = v.Students.Name,
                    excused=v.Students.excused,
                    v.Date,
                    v.comment,
                    VisitedBy = v.Users.UserName
                })
                .ToListAsync();

            if (!visitations.Any())
                return NotFound("No visitations found");

            return Ok(visitations);
        }
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task <IActionResult> edit(int id, [FromBody] EditVisitationDTO dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized("User not authenticated.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
            if (user == null) return Unauthorized("User not found.");

            var visit = await context.Visitations
                .Include(v => v.Students)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (visit == null) return NotFound("Visitation Not found");

            if (user.churchServiceID != visit.Students.churchServiceID)
                return StatusCode(403, "Not allowed to edit this Visitation");

            visit.comment = dto.comment;
            visit.userID = user.Id;
            await context.SaveChangesAsync();
            return Ok("visitation updated successfully");
        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized("User not authenticated.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
            if (user == null) return Unauthorized("User not found.");


            var visit = await context.Visitations
                .Include(v => v.Students)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (visit == null) return NotFound("Visitation not found.");

            if (visit.Students == null || user.churchServiceID != visit.Students.churchServiceID)
                return StatusCode(403, "Not allowed to delete this visitation.");

            context.Visitations.Remove(visit);
            await context.SaveChangesAsync();

            return Ok("Visitation deleted successfully.");
        }

    }
}
