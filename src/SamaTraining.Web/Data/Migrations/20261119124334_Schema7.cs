using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaTraining.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentAr",
                table: "Workshops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "Workshops",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Workshops",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_Phone",
                table: "Registrations",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_WorkshopId_Phone",
                table: "Registrations",
                columns: new[] { "WorkshopId", "Phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registrations_Phone",
                table: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_WorkshopId_Phone",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "ContentAr",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Workshops");
        }
    }
}
