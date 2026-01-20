using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class addImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opt2",
                table: "Students",
                newName: "image");

            migrationBuilder.AddColumn<string>(
                name: "confessor",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confessor",
                table: "Students");

            migrationBuilder.RenameColumn(
                name: "image",
                table: "Students",
                newName: "opt2");
        }
    }
}
