using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace church.Models
{
    public class context:DbContext
    {
        public DbSet<Churches> Churches { get; set; }
        public DbSet<Services> Services { get; set; }
        public DbSet<ChurchServices> ChurchServices { get; set; }
        public DbSet<Grades> Grades { get; set; }
        public DbSet<Students> Students { get; set; }
        public DbSet<Users> Users { get; set; }
        public DbSet<Attendance> Attendance { get; set; }
        public DbSet<Visitations> Visitations { get; set; }
        public DbSet <Subscriptions> Subscriptions { get; set; }
        public DbSet<DeletedStudents> DeletedStudents { get; set; }
        public context(DbContextOptions<context> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.studentID, a.Date })
                .IsUnique();
        }
    }
}
