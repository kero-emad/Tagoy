using church.Models;
using church.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace church.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IConfiguration config;
        context context;
        public UsersController(context _context, IConfiguration _config)
        {
            context = _context;
            config = _config;
        }
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDTO registerDTO)
        {
            if (context.Users.Any(u=>u.UserName==registerDTO.username)) 
                return BadRequest("UserName already exists");
            var churchService = await context.ChurchServices
             .FirstOrDefaultAsync(cs => cs.ChurchID == registerDTO.ChurchId && cs.ServiceID == registerDTO.ServiceId);

            if (churchService == null)
            {
                return BadRequest("This service is not linked to the selected church.");
            }
            var user = new Users
            {
                UserName = registerDTO.username,
                Password = registerDTO.password,
                roleId = registerDTO.roleId,
                IsAdmin = false,
                churchServiceID = churchService.Id,
                allowedGrades = registerDTO.AllowedGrades
            };
            context.Users.Add(user);
            context.SaveChanges();

            return Created();
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDTO loginDTO)
        {
            var user = await context.Users
       .Include(u => u.ChurchServices)
         .ThenInclude(cs => cs.Churches)
        .Include(u => u.ChurchServices)
         .ThenInclude(cs => cs.Services)
        .FirstOrDefaultAsync(u => u.UserName == loginDTO.username);

            if (user == null)
                return Unauthorized("user not found");
            if (user.Password != loginDTO.password)
                return Unauthorized("invalid password");
            var token = GenerateJwtToken(user);
            return Ok(new
            {
                Token = token,
                userid = user.Id,
                name = user.UserName,
                church = user.ChurchServices?.Churches?.churchName ?? "N/A",
                service = user.ChurchServices?.Services?.serviceName ?? "N/A",
                grades = user.allowedGrades,
                role=user.roleId
                
            });
        }
        [Authorize]
        [HttpPost("allusers")]
        public async Task<IActionResult> GetAllUsers(GetAllUsersDTO dto)
        {
            var churchservice = await context
                .ChurchServices
                .FirstOrDefaultAsync(cs => cs.ChurchID == dto.ChurchID && cs.ServiceID == dto.ServiceID);
            if (churchservice == null)
                return NotFound();
            var users = await context.Users
            .Where(u => u.churchServiceID == churchservice.Id)
            .Select(u => new
            {
            u.Id,
            u.UserName,
            u.allowedGrades,
            u.roleId
            })
            .ToListAsync();
            return Ok(users);

        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user=await context.Users.FirstOrDefaultAsync
                (u=>u.Id == id);
            if (user == null)
                return NotFound();
            context.Users.Remove(user);
            await context.SaveChangesAsync();
            return Ok("Deleted successfully");
        }
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task<IActionResult>EditUser(int id,[FromBody]EditUserDTO dto)
        {
            if (dto == null)
            {
                return BadRequest("Invalid user data.");
            }

            var user = await context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            // تحديث الحقول إذا كانت موجودة في DTO
            if (!string.IsNullOrEmpty(dto.UserName))
                user.UserName = dto.UserName;

            if (!string.IsNullOrEmpty(dto.Password))
            {
                user.Password = dto.Password;
            }

            if (dto.roleId.HasValue)
                user.roleId = dto.roleId.Value;

            if (dto.allowedGrades != null)
                user.allowedGrades = dto.allowedGrades;

            try
            {
                await context.SaveChangesAsync();
                return Ok("Updated successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private string GenerateJwtToken(Users user)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.UserName),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddYears(1),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }



    }
}
