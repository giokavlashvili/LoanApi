using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokensAndOtpUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OtpVerifications",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LoanApplications",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Keep the newest pending challenge per recipient+purpose, expire the rest (Status 2 =
            // Expired), or the unique index below fails to create against data with duplicates.
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Recipient, Purpose ORDER BY Created DESC, Id DESC) AS rn
                    FROM OtpVerifications WHERE Status = 0
                )
                UPDATE OtpVerifications SET Status = 2 WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);
            ");

            migrationBuilder.CreateIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Pending",
                table: "OtpVerifications",
                columns: new[] { "Recipient", "Purpose" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Pending",
                table: "OtpVerifications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OtpVerifications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LoanApplications");
        }
    }
}
