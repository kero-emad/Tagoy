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
    }
}
