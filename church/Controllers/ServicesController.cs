using church.Migrations;
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
    public class ServicesController : ControllerBase
    {
        context context;
        public ServicesController(context _context) 
        { 
            context = _context;
        }
        [Authorize]
        [HttpGet("show")]
        public async Task <IActionResult> getAllServices()
        {
            var services = await context.Services
                .Select(s => new ShowAllServicesDTO
            {
                Id = s.Id,
                Code = s.Code,
                serviceName=s.serviceName
            })
                .ToListAsync();
            return Ok(services);
        }
        [Authorize]
        [HttpPost("add")]
        public async Task <IActionResult> addService(AddServiceDTO dto)
        {
            if (context.Services.Any(s => s.serviceName == dto.serviceName))
                return BadRequest("serviceName already exists");
            if (context.Services.Any(s => s.Code == dto.Code))
                return BadRequest("Code already exists please try another code for the service");
            
            var service = new Services
            {
                serviceName = dto.serviceName,
                Code = dto.Code
            };
            context.Services.Add(service);
            await context.SaveChangesAsync();
            return Ok("Service added successfully");
        }
        [Authorize]
        [HttpPost("link")]
        public async Task <IActionResult> addChurchServices(AddChurchServiceDTO dto)
        {
            var churchExists = await context.Churches.AnyAsync(c => c.Id == dto.churchId);
            if (!churchExists)
                return NotFound("Church not found");

            var serviceExists = await context.Services.AnyAsync(s => s.Id == dto.serviceId);
            if (!serviceExists)
                return NotFound("Service not found");

            var alreadyLinked = await context.ChurchServices
                .AnyAsync(x => x.ChurchID == dto.churchId && x.ServiceID == dto.serviceId);

            if (alreadyLinked)
                return BadRequest("Service already linked to this church");

            var link = new ChurchServices
            {
                ChurchID = dto.churchId,
                ServiceID = dto.serviceId
            };

            context.ChurchServices.Add(link);
            await context.SaveChangesAsync();

            return Ok("Service linked to church successfully");
        }
        [Authorize]
        [HttpDelete("unlink/{churchId}/{serviceId}")]
        public async Task<IActionResult> removeChurchService(int churchId, int serviceId)
        {
            var link = await context.ChurchServices
                .FirstOrDefaultAsync(cs => cs.ChurchID == churchId && cs.ServiceID == serviceId);
            if (link == null)
                return NotFound("Service is not linked to this church");

            var hasStudents = await context.Students
                .AnyAsync(s => s.churchServiceID == link.Id);
            if (hasStudents)
                return BadRequest("This service has students in this church, delete or move them first");

            var hasUsers = await context.Users
                .AnyAsync(u => u.churchServiceID == link.Id);
            if (hasUsers)
                return BadRequest("This service has users in this church, delete them first");

            context.ChurchServices.Remove(link);
            await context.SaveChangesAsync();
            return Ok("Service unlinked from church successfully");
        }
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task<IActionResult> editService(int id, [FromBody] EditServiceDTO dto)
        {
            var service = await context.Services.FirstOrDefaultAsync(s => s.Id == id);
            if (service == null)
                return NotFound("service not found");

            if (dto.Code != null)
            {
                if (context.Services.Any(s => s.Code == dto.Code && s.Id != id))
                    return BadRequest("Code already exists please try another code for the service");
                service.Code = dto.Code;
            }

            if (dto.serviceName != null)
            {
                if (context.Services.Any(s => s.serviceName == dto.serviceName && s.Id != id))
                    return BadRequest("serviceName already exists");
                service.serviceName = dto.serviceName;
            }

            await context.SaveChangesAsync();
            return Ok("Service updated successfully");
        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> deleteService(int id)
        {
            var service = await context.Services
                .Include(s => s.ChurchServices)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (service == null)
                return NotFound("service not found");

            var linkedServiceIds = service.ChurchServices.Select(cs => cs.Id).ToList();
            var hasStudents = await context.Students
                .AnyAsync(s => linkedServiceIds.Contains(s.churchServiceID));
            if (hasStudents)
                return BadRequest("service has students, delete or move them first");

            var hasUsers = await context.Users
                .AnyAsync(u => linkedServiceIds.Contains(u.churchServiceID));
            if (hasUsers)
                return BadRequest("service has users, delete them first");

            if (linkedServiceIds.Count > 0)
                context.ChurchServices.RemoveRange(service.ChurchServices);

            context.Services.Remove(service);
            await context.SaveChangesAsync();
            return Ok("Deleted successfully");
        }
    }
}
