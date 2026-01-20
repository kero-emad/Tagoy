using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueConstraintToAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attendance_studentID",
                table: "Attendance");

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_studentID_Date",
                table: "Attendance",
                columns: new[] { "studentID", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attendance_studentID_Date",
                table: "Attendance");

            migrationBuilder.CreateIndex(
                name: "IX_Attendance_studentID",
                table: "Attendance",
                column: "studentID");
        }
    }
}
