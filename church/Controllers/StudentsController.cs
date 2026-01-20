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
    public class StudentsController : ControllerBase
    {
        context context;
        public StudentsController(context _context) 
        {
            context = _context;
        }

        [Authorize]
        [HttpPost("add")]
        public async Task<IActionResult> AddStudents([FromForm] AddStudentsDTO addStudentsDTO)
        {
            
            //var studentQR = await context.Students.FirstOrDefaultAsync
            //    (s=>s.QR==addStudentsDTO.qr);
            //if (studentQR != null) return BadRequest("Student-QR already registerd");
            //var grade = await context.Grades.FirstOrDefaultAsync
            //    (g => g.Name == addStudentsDTO.grade);
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to add new students");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");
            var churchservice = await context.ChurchServices
                .Include(c=>c.Churches)
                .Include(s=>s.Services).
                FirstOrDefaultAsync
                (cs => cs.Id==user.churchServiceID);
            if (churchservice == null)
                return NotFound("This service doesn't exisit in this church");

            //////// الاسم ميتكررش داخل نفس الخدمه والفصل 

            var nameExists = await context.Students.AnyAsync(s =>
                 s.Name == addStudentsDTO.name &&
                 s.churchServiceID == churchservice.Id &&
                 s.GradeId == addStudentsDTO.grade);

            if (nameExists)
                return BadRequest("This student name already exists in the same service and grade. Try a different name.");


            string? imagePath = null;

            if (addStudentsDTO.image != null && addStudentsDTO.image.Length > 0)
            {
                
                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/students");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // اسم الصورة يكون فريد باستخدام GUID
                var fileName = $"{Guid.NewGuid()}_{addStudentsDTO.image.FileName}";
                var fullPath = Path.Combine(folderPath, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await addStudentsDTO.image.CopyToAsync(stream);
                }

                imagePath = $"http:tagwi.runasp.net//images/students/{fileName}";
            }


            var student = new Students
            {
                //QR=addStudentsDTO.qr,
                Name = addStudentsDTO.name,
                phone=addStudentsDTO.phone,
                anotherPhone=addStudentsDTO.anotherPhone,
                CreatedAt= DateTime.Now,
                GradeId=addStudentsDTO.grade,
                address=addStudentsDTO.address,
                DateOfBirth = addStudentsDTO.DateOfBirth,
                Gender =addStudentsDTO.gender,
                churchServiceID=churchservice.Id,
                image=imagePath,
                confessor=addStudentsDTO.confessor,
                area=addStudentsDTO.area,
                location=addStudentsDTO.location,
                notes=addStudentsDTO.notes,
                excused=addStudentsDTO.excused,
                role=addStudentsDTO.role,
                details=addStudentsDTO.details,
                roleId=addStudentsDTO.roleId,
            };
            await context.Students.AddAsync(student);
            await context.SaveChangesAsync();

            var churchName = churchservice.Churches?.Code ?? "XX";
            var serviceName = churchservice.Services?.Code ?? "XX";

            string GetFirstTwo(string? input) => string.IsNullOrEmpty(input) ? "XX" :
                (input.Length >= 2 ? input.Substring(0, 2).ToUpper() : input.ToUpper().PadRight(2, 'X'));

            string qr = $"{GetFirstTwo(churchName)}-{GetFirstTwo(serviceName)}-{student.Id}";

            student.QR = qr;
            await context.SaveChangesAsync(); // Update QR

            return Ok("New student added with QR: " + qr);
        }
        [Authorize]
        [HttpGet("show-students/{grade}")]
        public async Task <IActionResult> showStudents(int grade) 
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login");
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");
            var churchservice = await context.ChurchServices.FirstOrDefaultAsync
                 (ch => ch.Id == user.churchServiceID);


            
            ///////////////فرق معينه في الخدمه
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(grade))
                {
                    return StatusCode(403, "You are not allowed to show students in this grade");
                }
            }
            
            
            var students = await context.Students.Where
                (s => s.churchServiceID == churchservice.Id &&
                s.GradeId == grade)
                .Select(g => new
                {
                    Id=g.Id,
                    QR=g.QR,
                    name = g.Name,
                    nameEnglish=g.NameEn,
                    grade=g.GradeId,
                    phone = g.phone,
                    anotherPhone=g.anotherPhone,
                    address = g.address,
                    area=g.area,
                    location=g.location,
                    notes=g.notes,
                    excused=g.excused,
                    role=g.role,
                    details=g.details,
                    roleId=g.roleId,
                    image =g.image,
                    DateOfBirth = g.DateOfBirth.HasValue ? g.DateOfBirth.Value.Date : (DateTime?)null,
                    gender = g.Gender,
                    confessor=g.confessor,
                    createdAt =g.CreatedAt

                }).ToListAsync();
            if (!students.Any())
                return NotFound("Not found students in this service");
            return Ok(students);
        }
        
        [Authorize]
        [HttpGet("show-student/{QR}")]
        public async Task<IActionResult> showDetails(string QR)
        {
            var student=await context.Students.Include(s => s.Grades).FirstOrDefaultAsync
                (s=>s.QR == QR);
            if (student == null) return NotFound("Student Not Found");

            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to add new students");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to add new students");

            if (user.churchServiceID != student.churchServiceID)
                return StatusCode(403, "Not allowed to show the details of this student");


            ///////////////فرق معينه في الخدمه
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                {
                    return StatusCode(403, "You are not allowed to show details of this student");

                }
            }

            var result = new ShowStudentDTO
            {
                Id=student.Id,
                QR = student.QR,
                name = student.Name,
                nameEn=student.NameEn,
                grade = student.GradeId,
                CreatedAt = student.CreatedAt,
                image=student.image,
                confessor=student.confessor,
                excused=student.excused,
                role=student.role,
                details=student.details,
                roleId=student.roleId,
                gender = student.Gender,
                phone = student.phone,
                anotherPhone = student.anotherPhone,
                address = student.address,
                area = student.area,
                location = student.location,
                notes=student.notes,
                DateOfBirth = student.DateOfBirth.HasValue ? student.DateOfBirth.Value.Date : (DateTime?)null
            };
            return Ok(result);
        }
        
        [Authorize]
        [HttpPut("edit/{id}")]
        public async Task<IActionResult> EditStudent(int id, [FromForm] EditStudentDTO editStudentDTO)
        {
            var student = await context.Students.Include(s => s.Grades).FirstOrDefaultAsync(s => s.Id == id);
            if (student == null)
                return NotFound("Student not found");

            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null)
                return Unauthorized ("User not authenticated");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null)
                return Unauthorized("User not found");

            if (student.churchServiceID != user.churchServiceID)
                return StatusCode(403, "Not allowed to edit this student");

            ///////////////فرق معينه في الخدمه
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                {
                  return StatusCode(403, "You are not allowed to edit this student’s grade.");
                }
            }


            if (!string.IsNullOrEmpty(editStudentDTO.name))
                student.Name = editStudentDTO.name;

            if (!string.IsNullOrEmpty(editStudentDTO.phone))
                student.phone = editStudentDTO.phone;

            if (!string.IsNullOrEmpty(editStudentDTO.anotherPhone))
                student.anotherPhone=editStudentDTO.anotherPhone;

            if (!string.IsNullOrEmpty(editStudentDTO.address))
                student.address = editStudentDTO.address;

            if (!string.IsNullOrEmpty(editStudentDTO.area))
                student.area = editStudentDTO.area;

            if (!string.IsNullOrEmpty(editStudentDTO.location))
                student.location = editStudentDTO.location;

            if (!string.IsNullOrEmpty(editStudentDTO.confessor))
                student.confessor= editStudentDTO.confessor;

            if (!string.IsNullOrEmpty(editStudentDTO.excused))
                student.excused = editStudentDTO.excused;

            if(!string.IsNullOrEmpty(editStudentDTO.role))
                student.role = editStudentDTO.role;

            if(!string.IsNullOrEmpty(editStudentDTO.details))
                student.details = editStudentDTO.details;

            if (!string.IsNullOrEmpty(editStudentDTO.notes))
                student.notes = editStudentDTO.notes;

            if (!string.IsNullOrEmpty(editStudentDTO.NameEn))
                student.NameEn = editStudentDTO.NameEn;

            if (editStudentDTO.roleId.HasValue)
                student.roleId = editStudentDTO.roleId;

            if (editStudentDTO.gender != null)
                student.Gender = editStudentDTO.gender;

            if (editStudentDTO.DateOfBirth.HasValue)
                student.DateOfBirth = editStudentDTO.DateOfBirth.Value.Date;


            if (editStudentDTO.grade.HasValue)
            {
                var grade = await context.Grades.FirstOrDefaultAsync(g => g.Id == editStudentDTO.grade);
                if (grade == null)
                    return NotFound("Grade not found");

                student.GradeId = grade.Id;
            }
            if (editStudentDTO.image != null && editStudentDTO.image.Length > 0)
            {
                // إنشاء مجلد الحفظ لو مش موجود
                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/students");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // اسم فريد للصورة الجديدة
                var newFileName = $"{Guid.NewGuid()}_{Path.GetFileName(editStudentDTO.image.FileName)}";
                var fullPath = Path.Combine(folderPath, newFileName);

                // حفظ الصورة الجديدة
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await editStudentDTO.image.CopyToAsync(stream);
                }

                // اختياري: حذف الصورة القديمة
                if (!string.IsNullOrEmpty(student.image))
                {
                    var oldImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", student.image.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                    if (System.IO.File.Exists(oldImagePath))
                        System.IO.File.Delete(oldImagePath);
                }

                // تحديث مسار الصورة
                student.image = $"http:tagwi.runasp.net//images/students/{newFileName}";
            }


            await context.SaveChangesAsync();
            return Ok("Student data updated successfully");
        }
        [Authorize]
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            var student = await context.Students.FirstOrDefaultAsync(s => s.Id == id);
            if (student == null)
                return NotFound("Student not found");

            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null)
                return Unauthorized("User not authenticated");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null)
                return Unauthorized("User not found");

            if (student.churchServiceID != user.churchServiceID)
                return StatusCode(403,"Not allowed to delete this student");

            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                {
               return StatusCode(403, "You are not allowed to delete this student’s grade.");
                }
            }
            var deletedStudent = new DeletedStudents
            {
                QR = student.QR,
                Name = student.Name,
                NameEn = student.NameEn,
                phone = student.phone,
                anotherPhone=student.anotherPhone,
                address = student.address,
                Gender = student.Gender,
                image = student.image,
                DateOfBirth = student.DateOfBirth,
                confessor = student.confessor,
                excused=student.excused,
                role=student.role,
                details=student.details,
                notes=student.notes,
                roleId=student.roleId,
                CreatedAt = student.CreatedAt,
                GradeId = student.GradeId,
                churchServiceID = student.churchServiceID,
                DeletedAt = DateTime.Now
            };
            context.DeletedStudents.Add(deletedStudent);
            await context.SaveChangesAsync();
            context.Students.Remove(student);
            await context.SaveChangesAsync();

            return Ok("Student deleted successfully");
        }

        [HttpGet("generate-qr")]
        public async Task<IActionResult> GenerateMissingQRCodes()
        {
            var studentsWithoutQR = await context.Students
                .Include(s => s.ChurchServices)
                    .ThenInclude(cs => cs.Churches)
                .Include(s => s.ChurchServices)
                    .ThenInclude(cs => cs.Services)
                .Where(s => string.IsNullOrEmpty(s.QR))
                .ToListAsync();

            if (!studentsWithoutQR.Any())
                return Ok("No students missing QR codes.");

            string GetFirstTwo(string? input) => string.IsNullOrEmpty(input)
                ? "XX"
                : (input.Length >= 2 ? input.Substring(0, 2).ToUpper() : input.ToUpper().PadRight(2, 'X'));

            foreach (var student in studentsWithoutQR)
            {
                var churchName = student.ChurchServices?.Churches?.Code ?? "XX";
                var serviceName = student.ChurchServices?.Services?.Code ?? "XX";

                if (serviceName== "SCOUTFestival")
                    student.QR = $"{GetFirstTwo(churchName)}-{"SCV"}-{student.Id}";
                else
                    student.QR = $"{GetFirstTwo(churchName)}-{GetFirstTwo(serviceName)}-{student.Id}";
            }

            await context.SaveChangesAsync();
            return Ok($"Generated QR codes for {studentsWithoutQR.Count} students.");
        }
        [Authorize]
        [HttpGet("show-student-details/{QR}")]
        public async Task<IActionResult> ShowFullStudentDetails(string QR)
        {
            var userID = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userID == null) return Unauthorized("user not found please login to continue");

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userID));
            if (user == null) return Unauthorized("user not found please login to continue");

            // جلب الطالب
            var student = await context.Students
                .Include(s => s.Grades)
                .FirstOrDefaultAsync(s => s.QR == QR);

            if (student == null) return NotFound("Student Not Found");

            // تحقق من الخدمة
            if (user.churchServiceID != student.churchServiceID)
                return StatusCode(403, "Not allowed to show this student's details");

            // تحقق من المراحل المسموح بها
            if (user.allowedGrades != null && user.allowedGrades.Any())
            {
                if (!user.allowedGrades.Contains(student.GradeId))
                    return StatusCode(403, "You are not allowed to view this student's details");
            }

            // بيانات الطالب الأساسية
            var studentInfo = new
            {
                Id = student.Id,
                QR = student.QR,
                name = student.Name,
                nameEn = student.NameEn,
                grade = student.GradeId,
                CreatedAt = student.CreatedAt,
                image = student.image,
                confessor = student.confessor,
                excused=student.excused,
                role=student.role,
                details=student.details,
                roleId=student.roleId,
                gender = student.Gender,
                phone = student.phone,
                anotherPhone=student.anotherPhone,
                address = student.address,
                area = student.area,
                location = student.location,
                notes=student.notes,
                DateOfBirth = student.DateOfBirth.HasValue ? student.DateOfBirth.Value.Date : (DateTime?)null
            };

            // الأيام اللي حضرها فقط
            var attendanceDates = await context.Attendance
                .Where(a => a.studentID == student.Id && a.Status==Status.Present)
                .Select(a => a.Date.Date)
                .OrderBy(d => d)
                .ToListAsync();

            // الاشتراكات المدفوعة
            var paidSubscriptions = await context.Subscriptions
                .Where(s => s.studentID == student.Id && s.isPaid == true)
                .Include(s => s.Users)
                .Select(s => new
                {
                    s.month,
                    s.year,
                    s.LastUpdated,
                    userName = s.Users.UserName
                })
                .OrderByDescending(s => s.year)
                .ThenByDescending(s => s.month)
                .ToListAsync();

            // الافتقادات الخاصة بالطالب
            var visitations = await context.Visitations
                .Where(v => v.studentID == student.Id)
                .Include(v => v.Users)
                .Select(v => new
                {
                    v.Date,
                    v.comment,
                    userName = v.Users.UserName
                })
                .OrderByDescending(v => v.Date)
                .ToListAsync();

            // النتيجة النهائية
            var result = new
            {
                studentInfo,
                attendanceDates,
                paidSubscriptions,
                visitations
            };

            return Ok(result);
        }

    }
}
