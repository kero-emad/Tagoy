using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class addRoleDetailsRoleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "details",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "roleId",
                table: "Students",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "details",
                table: "DeletedStudents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "DeletedStudents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "roleId",
                table: "DeletedStudents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "details",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "role",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "roleId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "details",
                table: "DeletedStudents");

            migrationBuilder.DropColumn(
                name: "role",
                table: "DeletedStudents");

            migrationBuilder.DropColumn(
                name: "roleId",
                table: "DeletedStudents");
        }
    }
}
