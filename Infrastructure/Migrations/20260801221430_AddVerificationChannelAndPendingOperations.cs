using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationChannelAndPendingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Pending",
                table: "OtpVerifications");

            migrationBuilder.AlterColumn<string>(
                name: "Recipient",
                table: "OtpVerifications",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "OtpVerifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PendingOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Channel_Pending",
                table: "OtpVerifications",
                columns: new[] { "Recipient", "Purpose", "Channel" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PendingOperations_ChallengeId",
                table: "PendingOperations",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingOperations_OperationId",
                table: "PendingOperations",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingOperations_Status_Created",
                table: "PendingOperations",
                columns: new[] { "Status", "Created" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingOperations");

            migrationBuilder.DropIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Channel_Pending",
                table: "OtpVerifications");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "OtpVerifications");

            migrationBuilder.AlterColumn<string>(
                name: "Recipient",
                table: "OtpVerifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.CreateIndex(
                name: "UX_OtpVerifications_Recipient_Purpose_Pending",
                table: "OtpVerifications",
                columns: new[] { "Recipient", "Purpose" },
                unique: true,
                filter: "[Status] = 0");
        }
    }
}
