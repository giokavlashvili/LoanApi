using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Enhance_Logs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientIp",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "Logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "Logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Properties",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHeaders",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusCode",
                table: "Logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "Logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientIp",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "HttpMethod",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Properties",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestHeaders",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "StatusCode",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "Logs");
        }
    }
}
