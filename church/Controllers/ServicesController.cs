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
    }
}
