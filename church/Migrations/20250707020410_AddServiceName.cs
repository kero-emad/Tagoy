using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "serviceName",
                table: "Services",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "serviceName",
                table: "Services");
        }
    }
}
