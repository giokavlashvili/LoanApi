using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SerilogLogColumnAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Logger",
                table: "Logs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Level",
                table: "Logs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            // Serilog spells levels out where NLog abbreviated them. Without this, the table
            // carries two vocabularies at once and every saved query filtering Level = 'Info'
            // silently stops matching rows written after the cutover. Runs after the widening
            // above because 'Information' does not fit in the old nvarchar(10).
            migrationBuilder.Sql(@"
                UPDATE Logs SET Level = 'Information' WHERE Level = 'Info';
                UPDATE Logs SET Level = 'Warning'     WHERE Level = 'Warn';
                UPDATE Logs SET Level = 'Verbose'     WHERE Level = 'Trace';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Must precede the narrowing below: 'Information' is 11 characters, so the
            // ALTER back to nvarchar(10) fails outright while any such row remains.
            migrationBuilder.Sql(@"
                UPDATE Logs SET Level = 'Info'  WHERE Level = 'Information';
                UPDATE Logs SET Level = 'Warn'  WHERE Level = 'Warning';
                UPDATE Logs SET Level = 'Trace' WHERE Level = 'Verbose';");

            migrationBuilder.AlterColumn<string>(
                name: "Logger",
                table: "Logs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Level",
                table: "Logs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16);
        }
    }
}
