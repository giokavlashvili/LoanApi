using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceLogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Trace",
                table: "Logs");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Logs",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Logger",
                table: "Logs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Exception",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Channel",
                table: "Logs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientIp",
                table: "Logs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "Logs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "Logs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "Logs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "Logs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusCode",
                table: "Logs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Logs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "Logs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Channel_Level_When",
                table: "Logs",
                columns: new[] { "Channel", "Level", "When" });

            migrationBuilder.CreateIndex(
                name: "IX_Logs_CorrelationId",
                table: "Logs",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_StatusCode_When",
                table: "Logs",
                columns: new[] { "StatusCode", "When" },
                filter: "[StatusCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_When",
                table: "Logs",
                column: "When");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Logs_Channel_Level_When",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_CorrelationId",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_StatusCode_When",
                table: "Logs");

            migrationBuilder.DropIndex(
                name: "IX_Logs_When",
                table: "Logs");

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
                name: "MachineName",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "StatusCode",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "Logs");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Logger",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Exception",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Channel",
                table: "Logs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trace",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
