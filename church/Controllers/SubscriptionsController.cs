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
    public class SubscriptionsController : ControllerBase
    {
        context context;
        public SubscriptionsController(context _context) 
        {
            context = _context;
        }
        [Authorize]
        [HttpPost("add")]
        public async Task<IActionResult> addSubscription (AddSubscriptionsDTO dto) 
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("User not found, please login.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("User not found, please login.");

            if (user.allowedGrades != null && user.allowedGrades.Any() && !user.allowedGrades.Contains(dto.grade))
                return StatusCode(403, "You are not allowed to manage subscriptions for this grade.");

            int month = dto.month ?? DateTime.Now.Month;
            int year = dto.year ?? DateTime.Now.Year;

            var studentsInGrade = await context.Students
                .Where(s => s.churchServiceID == user.churchServiceID && s.GradeId == dto.grade)
                .ToListAsync();

            var studentQrsSet = dto.students?.ToHashSet() ?? new HashSet<string>();

            // الاشتراكات المسجلة مسبقًا للطلاب في هذا الشهر والسنة
            var existingSubscriptions = await context.Subscriptions
                .Where(sub => sub.month == month && sub.year == year && studentsInGrade.Select(s => s.Id).Contains(sub.studentID))
                .ToListAsync();

            var newSubscriptions = new List<Subscriptions>();
            bool anyChanges = false;

            foreach (var student in studentsInGrade)
            {
                // لو الطالب موجود في الطلب الحالي
                if (studentQrsSet.Contains(student.QR))
                {
                    var existing = existingSubscriptions.FirstOrDefault(s => s.studentID == student.Id);

                    if (existing != null)
                    {
                        // لو هو بالفعل مدفوع ما نعدلوش
                        if (!existing.isPaid)
                        {
                            existing.isPaid = true;
                            existing.userID = user.Id;
                            existing.LastUpdated = DateTime.Now;
                            anyChanges = true;
                        }
                    }
                    else
                    {
                        // إضافة اشتراك جديد مدفوع
                        newSubscriptions.Add(new Subscriptions
                        {
                            studentID = student.Id,
                            userID = user.Id,
                            month = month,
                            year = year,
                            isPaid = true,
                            LastUpdated = DateTime.Now
                        });
                        anyChanges = true;
                    }
                }
                // الطلاب اللي مش في الليست ما نعملهمش أي تعديل إطلاقًا
            }

            if (newSubscriptions.Any())
                context.Subscriptions.AddRange(newSubscriptions);

            if (anyChanges)
            {
                await context.SaveChangesAsync();
                return Ok("Subscriptions updated successfully.");
            }
            else
            {
                return Ok("No changes done.");
            }

        }

        [HttpPost("show")]
        [Authorize]
        public async Task<IActionResult> ShowSubscriptions(ShowSubscriptioDTO dto)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null)
                return Unauthorized("user not found please login to add new students");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null)
                return Unauthorized("user not found please login to add new students");

            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(dto.grade))
                {
                    return StatusCode(403, "You are not allowed to view Subscriptions for this grade.");
                }
            }

            int month = dto.month ?? DateTime.Now.Month;
            int year = dto.year ?? DateTime.Now.Year;

            // كل الطلاب في المرحلة المحددة
            var students = await context.Students
                .Where(s => s.churchServiceID == user.churchServiceID && s.GradeId == dto.grade)
                .ToListAsync();

            var studentIds = students.Select(s => s.Id).ToList();

            // الاشتراكات الموجودة لهذا الشهر
            var subscriptions = await context.Subscriptions
                .Where(s => s.month == month && s.year == year && studentIds.Contains(s.studentID))
                .Include(s => s.Users)
                .ToListAsync();

            // نعمل دمج بين الطلاب والاشتراكات (Left Join)
            var result = students.Select(student =>
            {
                var sub = subscriptions.FirstOrDefault(s => s.studentID == student.Id);

                return new
                {
                    studentId = student.Id,
                    studentQr=student.QR,
                    studentName = student.Name,
                    excused=student.excused,
                    isPaid = sub != null && sub.isPaid,
                    SubscriptionId = sub?.Id,
                    LastUpdated = sub?.LastUpdated,
                    userName = sub?.Users?.UserName ?? "—"
                };
            });

            return Ok(result);
        }


        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task <IActionResult> editSubscription (int id)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("User not found, please login.");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("User not found, please login.");

            var sub = await context.Subscriptions
        .Include(s => s.Students)
        .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null)
                return NotFound("Subscription not found.");

            // صلاحية الخدمة
            if (sub.Students.churchServiceID != user.churchServiceID)
                return StatusCode(403, "You are not allowed to edit this subscription.");

            // صلاحية المرحلة
            if (user.allowedGrades != null && user.allowedGrades.Any()
                && !user.allowedGrades.Contains(sub.Students.GradeId))
            {
                return StatusCode(403, "You are not allowed to edit this grade's subscription.");
            }

            // التبديل من false إلى true أو العكس
            sub.isPaid = !sub.isPaid;
            sub.LastUpdated = DateTime.Now;
            sub.userID = user.Id;

            await context.SaveChangesAsync();

            return Ok("Updated successfully");
        }
        [Authorize]
        [HttpGet("show/{QR}")]
        public async Task<IActionResult> showSubscribtionsForStudent(string QR) 
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to show subscriptions");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to show subscriptions");

            var student = await context.Students.FirstOrDefaultAsync
                (s => s.QR == QR);
            if (student == null) return NotFound("Student Not Found");

            if (user.churchServiceID != student.churchServiceID)
                return StatusCode(403, "Not allowed to show the subscriptions of this student");

            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                {
                    return StatusCode(403, "You are not allowed to view subscriptions for this grade.");
                }
            }

            var subscriptions = await context.Subscriptions
                .Where(s => s.studentID == student.Id && s.isPaid == true)
                .Include(s=>s.Students)
                .Include(u=>u.Users)
                .Select(s=>new
                {
                    s.Students.Id,
                    s.Students.Name,
                    s.Students.QR,
                    s.Students.excused,
                    s.month,
                    s.year,
                    s.LastUpdated,
                    s.Users.UserName,
                    s.isPaid
                }).ToListAsync();
            return Ok(subscriptions);

        }
    }
}
