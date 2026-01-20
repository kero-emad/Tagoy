using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class editServiceNameCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Services",
                newName: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Services",
                newName: "Name");
        }
    }
}
