using church.Models;
using Microsoft.EntityFrameworkCore;

namespace church.Data;

/// <summary>
/// Creates the stage accounts for the Abotig CHS-T service when they do not exist.
/// UserName is used as the login identifier by the existing API.
/// </summary>
public static class ChoirStageAccountsSeeder
{
    private sealed record Account(string Email, string Password, string[] GradeAliases);

    private static readonly Account[] Accounts =
    {
        new("kindergarten@tagoy.com", "kindergarten#Q7v9", new[] { "حضانة", "روضة", "kindergarten", "kg" }),
        new("primary1@tagoy.com", "primary1#N4x8", new[] { "أولى ابتدائي", "اولى ابتدائي", "الصف الأول الابتدائي", "primary 1" }),
        new("primary2@tagoy.com", "primary2#H9k3", new[] { "ثانية ابتدائي", "تانية ابتدائي", "الصف الثاني الابتدائي", "primary 2" }),
        new("primary3@tagoy.com", "primary3#B6s2", new[] { "ثالثة ابتدائي", "تالتة ابتدائي", "الصف الثالث الابتدائي", "primary 3" }),
        new("primary4@tagoy.com", "primary4#V5q9", new[] { "رابعة ابتدائي", "رابعه ابتدائي", "الصف الرابع الابتدائي", "primary 4" }),
        new("primary5@tagoy.com", "primary5#J8r4", new[] { "خامسة ابتدائي", "الصف الخامس الابتدائي", "primary 5" }),
        new("primary6@tagoy.com", "primary6#C3m7", new[] { "سادسة ابتدائي", "الصف السادس الابتدائي", "primary 6" }),
        new("prep1@tagoy.com", "prep1#L6t2", new[] { "أولى إعدادي", "اولى اعدادي", "الصف الأول الإعدادي", "prep 1" }),
        new("prep2@tagoy.com", "prep2#R9f5", new[] { "ثانية إعدادي", "تانية اعدادي", "الصف الثاني الإعدادي", "prep 2" }),
        new("prep3@tagoy.com", "prep3#M4z8", new[] { "ثالثة إعدادي", "تالتة اعدادي", "الصف الثالث الإعدادي", "prep 3" }),
        new("secondary1@tagoy.com", "secondary1#T7n3", new[] { "أولى ثانوي", "اولى ثانوي", "الصف الأول الثانوي", "secondary 1" }),
        new("secondary2@tagoy.com", "secondary2#P2w8", new[] { "ثانية ثانوي", "تانية ثانوي", "الصف الثاني الثانوي", "secondary 2" }),
        new("secondary3@tagoy.com", "secondary3#Y5d9", new[] { "ثالثة ثانوي", "تالتة ثانوي", "الصف الثالث الثانوي", "secondary 3" }),
        new("adminCHST@tagoy.com", "adminCHST#A7", Array.Empty<string>()),
        new("remonTharwatCHST@tagoy.com", "remonTharwatCHST#R4", Array.Empty<string>()),
        new("meladGergesCHST@tagoy.com", "meladGergesCHST#M6", Array.Empty<string>())
    };

    public static async Task SeedAsync(context db)
    {
        var churchServices = await db.ChurchServices
            .Include(x => x.Churches)
            .Include(x => x.Services)
            .ToListAsync();
        var churchService = churchServices.FirstOrDefault(x =>
                (x.Services.Code ?? "").Equals("CHS-T", StringComparison.OrdinalIgnoreCase) ||
                ((x.Services.serviceName ?? "").Contains("خورس") &&
                 (x.Services.serviceName ?? "").Contains("أبوتيج")));

        if (churchService is null)
            return;

        var grades = await db.Set<Grades>().ToListAsync();
        foreach (var account in Accounts)
        {
            var grade = grades.FirstOrDefault(g => account.GradeAliases.Any(alias =>
                g.Name.Contains(alias, StringComparison.OrdinalIgnoreCase)));

            var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == account.Email);
            if (user is null)
            {
                db.Users.Add(new Users
                {
                    UserName = account.Email,
                    Password = account.Password,
                    IsAdmin = false,
                    roleId = account.GradeAliases.Length == 0 ? 6 : 5,
                    churchServiceID = churchService.Id,
                    allowedGrades = grade is null ? new List<int>() : new List<int> { grade.Id }
                });
            }
            else if (user.Password != account.Password || user.roleId != (account.GradeAliases.Length == 0 ? 6 : 5) || user.churchServiceID != churchService.Id ||
                     (grade is not null && (user.allowedGrades?.Count != 1 || user.allowedGrades[0] != grade.Id)))
            {
                user.Password = account.Password;
                user.roleId = account.GradeAliases.Length == 0 ? 6 : 5;
                user.churchServiceID = churchService.Id;
                if (grade is not null)
                    user.allowedGrades = new List<int> { grade.Id };
            }
        }

        await db.SaveChangesAsync();
    }
}
