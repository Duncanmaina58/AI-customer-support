using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Api.Infrastructure.Persistence;

#nullable disable

namespace Api.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 9 — AI Action Engine: ActionDefinitions (a company's registered
    /// webhook actions), ActionLogs (the immutable audit trail — see
    /// AppDbContext.SaveChangesAsync for the runtime enforcement that backs up
    /// the DB-level design here), and OtpVerifications (identity verification
    /// state for RequiresVerification actions).
    ///
    /// NOTE ON PROVENANCE: hand-authored for the same reason as
    /// 20260711120000_WebCrawling.cs and 20260712090000_AuthHardening.cs — no
    /// dotnet SDK/NuGet-restorable environment available when this was written.
    /// Derived directly from ActionDefinitionConfiguration/ActionLogConfiguration/
    /// OtpVerificationConfiguration, and AppDbContextModelSnapshot.cs was updated
    /// to match. Run `dotnet ef migrations add VerifyActionEngine` once against a
    /// real dev database and confirm it comes back empty before deploying.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260713100000_ActionEngine")]
    public partial class ActionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- ActionDefinitions ----
            migrationBuilder.CreateTable(
                name: "ActionDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    WebhookSecretEncrypted = table.Column<string>(type: "text", nullable: false),
                    RequiresVerification = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ParameterSchema = table.Column<string>(type: "text", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionDefinitions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_CompanyId_ActionType",
                table: "ActionDefinitions",
                columns: new[] { "CompanyId", "ActionType" },
                unique: true);

            // ---- ActionLogs (immutable audit trail) ----
            migrationBuilder.CreateTable(
                name: "ActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParametersEncrypted = table.Column<string>(type: "text", nullable: false),
                    IdentityVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VerificationMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "None"),
                    WebhookStatusCode = table.Column<int>(type: "integer", nullable: true),
                    WebhookResponseJson = table.Column<string>(type: "text", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionLogs_ActionDefinitions_ActionDefinitionId",
                        column: x => x.ActionDefinitionId,
                        principalTable: "ActionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActionLogs_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionLogs_CompanyId_ExecutedAt",
                table: "ActionLogs",
                columns: new[] { "CompanyId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionLogs_ConversationId",
                table: "ActionLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionLogs_ActionDefinitionId",
                table: "ActionLogs",
                column: "ActionDefinitionId");

            // ---- OtpVerifications ----
            migrationBuilder.CreateTable(
                name: "OtpVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OtpHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PendingParametersJson = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpVerifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OtpVerifications_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_ConversationId_VerifiedAt_ExpiresAt",
                table: "OtpVerifications",
                columns: new[] { "ConversationId", "VerifiedAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OtpVerifications");
            migrationBuilder.DropTable(name: "ActionLogs");
            migrationBuilder.DropTable(name: "ActionDefinitions");
        }
    }
}
