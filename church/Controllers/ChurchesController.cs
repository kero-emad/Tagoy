using church.Models;
using church.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace church.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChurchesController : ControllerBase
    {
        context context;
        public ChurchesController(context _context)
        {
            context = _context;
        }
        [Authorize]
        [HttpGet("show")]
        public async Task <IActionResult> getAllChurches()
        {
            var churches = await context.Churches
        .Select(c => new ShowAllChurchesDTO
        {
            Id = c.Id,
            Code = c.Code,
            churchName=c.churchName
        })
        .ToListAsync();
            return Ok (churches);
        }
        [Authorize]
        [HttpPost("add")]
        public async Task<IActionResult> addChurch([FromBody] AddChurchDTO dto)
        {
            if (context.Churches.Any(c => c.churchName == dto.churchName))
                return BadRequest("churchName already exists");
            if(context.Churches.Any(u => u.Code == dto.Code))
                return BadRequest("Code already exists please try another code for the church");
            
            var church = new Churches
            {
                Code = dto.Code,
                churchName = dto.churchName
            };
            context.Churches.Add(church);
            await context.SaveChangesAsync();
            return Ok("Church added successfully");
        }
        [Authorize]
        [HttpGet("{id}/services")]
        public async Task <IActionResult> GetChurchServices(int id)
        {
            var church = await context.Churches
                .Include(c => c.ChurchServices)
                .ThenInclude(cs => cs.Services)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (church == null) {
                return NotFound("church not found");
            }
            var services = church.ChurchServices
            .Select(cs => new
            {
                ServiceID = cs.ServiceID,
                Code = cs.Services.Code,
                ServiceName = cs.Services.serviceName
            });

            return Ok(services);
        }
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task<IActionResult> editChurch(int id, [FromBody] EditChurchDTO dto)
        {
            var church = await context.Churches.FirstOrDefaultAsync(c => c.Id == id);
            if (church == null)
                return NotFound("church not found");

            if (dto.Code != null)
            {
                if (context.Churches.Any(c => c.Code == dto.Code && c.Id != id))
                    return BadRequest("Code already exists please try another code for the church");
                church.Code = dto.Code;
            }

            if (dto.churchName != null)
            {
                if (context.Churches.Any(c => c.churchName == dto.churchName && c.Id != id))
                    return BadRequest("churchName already exists");
                church.churchName = dto.churchName;
            }

            await context.SaveChangesAsync();
            return Ok("Church updated successfully");
        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> deleteChurch(int id)
        {
            var church = await context.Churches
                .Include(c => c.ChurchServices)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (church == null)
                return NotFound("church not found");

            var linkedServiceIds = church.ChurchServices.Select(cs => cs.Id).ToList();
            var hasStudents = await context.Students
                .AnyAsync(s => linkedServiceIds.Contains(s.churchServiceID));
            if (hasStudents)
                return BadRequest("church has students, delete or move them first");

            var hasUsers = await context.Users
                .AnyAsync(u => linkedServiceIds.Contains(u.churchServiceID));
            if (hasUsers)
                return BadRequest("church has users, delete them first");

            if (linkedServiceIds.Count > 0)
                context.ChurchServices.RemoveRange(church.ChurchServices);

            context.Churches.Remove(church);
            await context.SaveChangesAsync();
            return Ok("Deleted successfully");
        }
    }
}
