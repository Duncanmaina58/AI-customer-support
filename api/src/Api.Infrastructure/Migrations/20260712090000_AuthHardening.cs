using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Api.Infrastructure.Persistence;

#nullable disable

namespace Api.Infrastructure.Migrations
{
    /// <summary>
    /// Auth hardening: email verification, password reset, account lockout, and
    /// sign-in tracking columns on Agents, plus the new AgentSecurityTokens table
    /// (shared by the email-verification and password-reset flows).
    ///
    /// NOTE ON PROVENANCE: hand-authored for the same reason as
    /// 20260711120000_WebCrawling.cs — no dotnet SDK/NuGet-restorable
    /// environment available when this was written. Derived directly from
    /// AgentConfiguration/AgentSecurityTokenConfiguration, and
    /// AppDbContextModelSnapshot.cs was updated to match. Still worth running
    /// `dotnet ef migrations add VerifyAuthHardening` once against a real dev
    /// database and confirming it comes back empty before deploying.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260712090000_AuthHardening")]
    public partial class AuthHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedOutUntil",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLoginIp",
                table: "Agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAtUtc",
                table: "Agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentSecurityTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedFromIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSecurityTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSecurityTokens_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSecurityTokens_TokenHash_Type",
                table: "AgentSecurityTokens",
                columns: new[] { "TokenHash", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSecurityTokens_AgentId",
                table: "AgentSecurityTokens",
                column: "AgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentSecurityTokens");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LockedOutUntil",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastLoginIp",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastLoginAtUtc",
                table: "Agents");
        }
    }
}
