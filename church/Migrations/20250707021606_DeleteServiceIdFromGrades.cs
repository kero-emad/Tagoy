using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace church.Migrations
{
    /// <inheritdoc />
    public partial class DeleteServiceIdFromGrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Grades_Services_ServiceId",
                table: "Grades");

            migrationBuilder.DropIndex(
                name: "IX_Grades_ServiceId",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "Grades");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceId",
                table: "Grades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_ServiceId",
                table: "Grades",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Grades_Services_ServiceId",
                table: "Grades",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
