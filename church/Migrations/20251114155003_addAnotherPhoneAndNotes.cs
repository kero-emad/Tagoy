using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class addAnotherPhoneAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "anotherPhone",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "anotherPhone",
                table: "DeletedStudents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "DeletedStudents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anotherPhone",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "anotherPhone",
                table: "DeletedStudents");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "DeletedStudents");
        }
    }
}
