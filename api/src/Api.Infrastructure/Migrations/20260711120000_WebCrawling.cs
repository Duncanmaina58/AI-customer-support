using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Api.Infrastructure.Persistence;

#nullable disable

namespace Api.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 4 web crawling addition: three new KnowledgeChunks columns
    /// (SourceType/SourceUrl/WebSourceId) plus the WebSources and WebPages tables.
    ///
    /// NOTE ON PROVENANCE: this migration was authored by hand rather than via
    /// `dotnet ef migrations add`, because no .NET SDK / NuGet-restorable
    /// environment was available in the sandbox this was written in. The SQL
    /// below was derived directly from the corresponding EF Core configuration
    /// classes (WebSourceConfiguration, WebPageConfiguration, and the Sprint-4
    /// additions to KnowledgeChunkConfiguration) so it matches what
    /// `dotnet ef migrations add` would itself generate. Still, run
    /// `dotnet ef migrations has-pending-model-changes` (or just
    /// `dotnet ef migrations add VerifyWebCrawling` and confirm it comes back
    /// empty) once against a real dev database before deploying, to double-check
    /// this by-hand migration and the regenerated AppDbContextModelSnapshot.cs
    /// agree with the current model.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260711120000_WebCrawling")]
    public partial class WebCrawling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- KnowledgeChunks: three new nullable-by-default columns ----
            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "KnowledgeChunks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Document");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "KnowledgeChunks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WebSourceId",
                table: "KnowledgeChunks",
                type: "uuid",
                nullable: true);

            // ---- WebSources ----
            migrationBuilder.CreateTable(
                name: "WebSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CrawlMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CrawlDepth = table.Column<int>(type: "integer", nullable: false),
                    IncludePattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExcludePattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxPages = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PagesCrawled = table.Column<int>(type: "integer", nullable: false),
                    ChunksCreated = table.Column<int>(type: "integer", nullable: false),
                    MonitoringMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FixedIntervalHours = table.Column<int>(type: "integer", nullable: true),
                    NotifyOnChange = table.Column<bool>(type: "boolean", nullable: false),
                    LastCrawledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    HasJsRenderedPagesWarning = table.Column<bool>(type: "boolean", nullable: false),
                    MaxPagesReached = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentCrawlUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EstimatedTotalPages = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- WebPages ----
            migrationBuilder.CreateTable(
                name: "WebPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentLength = table.Column<int>(type: "integer", nullable: false),
                    HttpETag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HttpLastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckCount = table.Column<int>(type: "integer", nullable: false),
                    ChangeCount = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebPages_WebSources_WebSourceId",
                        column: x => x.WebSourceId,
                        principalTable: "WebSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebPages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ---- KnowledgeChunks.WebSourceId FK (added after WebSources exists) ----
            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeChunks_WebSources_WebSourceId",
                table: "KnowledgeChunks",
                column: "WebSourceId",
                principalTable: "WebSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ---- Indexes ----
            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_CompanyId_SourceUrl",
                table: "KnowledgeChunks",
                columns: new[] { "CompanyId", "SourceUrl" },
                filter: "\"SourceUrl\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_WebSourceId",
                table: "KnowledgeChunks",
                column: "WebSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_WebSources_CompanyId_Url",
                table: "WebSources",
                columns: new[] { "CompanyId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPages_WebSourceId_Url",
                table: "WebPages",
                columns: new[] { "WebSourceId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPages_Status_NextCheckAt",
                table: "WebPages",
                columns: new[] { "Status", "NextCheckAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebPages_CompanyId_LastChangedAt",
                table: "WebPages",
                columns: new[] { "CompanyId", "LastChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeChunks_WebSources_WebSourceId",
                table: "KnowledgeChunks");

            migrationBuilder.DropTable(
                name: "WebPages");

            migrationBuilder.DropTable(
                name: "WebSources");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeChunks_CompanyId_SourceUrl",
                table: "KnowledgeChunks");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeChunks_WebSourceId",
                table: "KnowledgeChunks");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "KnowledgeChunks");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "KnowledgeChunks");

            migrationBuilder.DropColumn(
                name: "WebSourceId",
                table: "KnowledgeChunks");
        }
    }
}
