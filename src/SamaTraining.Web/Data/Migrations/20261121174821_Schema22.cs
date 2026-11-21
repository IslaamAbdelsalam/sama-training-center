using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaTraining.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema22 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "WorkshopStatusHistory",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Registrations",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Registrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Registrations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Registrations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "WorkshopStatusHistory");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Registrations");
        }
    }
}
