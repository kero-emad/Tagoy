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
    public class GradesController : ControllerBase
    {
        context context;
        public GradesController(context _context)
        {
            context = _context;
        }
        [Authorize]
        [HttpGet("show")]
        public async Task<IActionResult> getAllGrades()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Users? user = null;

            if (int.TryParse(userId, out var userIdValue))
            {
                user = await context.Users.FirstOrDefaultAsync(
                    x => x.Id == userIdValue);
            }

            var query = context.Grades.AsQueryable();

            // An empty allowedGrades list means all grades.  When it
            // contains values, expose only grades the current user can
            // actually read; this also keeps AI grade suggestions safe
            // and relevant.
            if (user?.allowedGrades is { Count: > 0 } allowedGrades)
            {
                query = query.Where(
                    grade => allowedGrades.Contains(grade.Id));
            }

            var grades = await query
                .OrderBy(g => g.Name)
                .Select(g => new ShowAllGradesDTO
                {
                    Id = g.Id,
                    Name = g.Name
                })
                .ToListAsync();
            return Ok(grades);
        }
        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> getGrade(int id)
        {
            var grade = await context.Grades
                .Where(g => g.Id == id)
                .Select(g => new ShowAllGradesDTO
                {
                    Id = g.Id,
                    Name = g.Name
                })
                .FirstOrDefaultAsync();
            if (grade == null)
                return NotFound("grade not found");
            return Ok(grade);
        }
        [Authorize]
        [HttpPost("add")]
        public async Task<IActionResult> addGrade([FromBody] AddGradeDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Name is required");

            if (context.Grades.Any(g => g.Name == dto.Name))
                return BadRequest("Name already exists");

            var grade = new Grades
            {
                Name = dto.Name
            };
            context.Grades.Add(grade);
            await context.SaveChangesAsync();
            return Ok("Grade added successfully");
        }
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task<IActionResult> editGrade(int id, [FromBody] EditGradeDTO dto)
        {
            var grade = await context.Grades.FirstOrDefaultAsync(g => g.Id == id);
            if (grade == null)
                return NotFound("grade not found");

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                if (context.Grades.Any(g => g.Name == dto.Name && g.Id != id))
                    return BadRequest("Name already exists");
                grade.Name = dto.Name;
            }

            await context.SaveChangesAsync();
            return Ok("Grade updated successfully");
        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> deleteGrade(int id)
        {
            var grade = await context.Grades.FirstOrDefaultAsync(g => g.Id == id);
            if (grade == null)
                return NotFound("grade not found");

            var hasStudents = await context.Students.AnyAsync(s => s.GradeId == id);
            if (hasStudents)
                return BadRequest("grade has students, delete or move them first");

            context.Grades.Remove(grade);
            await context.SaveChangesAsync();
            return Ok("Deleted successfully");
        }
    }
}
