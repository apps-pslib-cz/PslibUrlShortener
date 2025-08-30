using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PslibUrlShortener.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Domain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OwnerSub = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    ActiveFromUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ActiveToUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    LastAccessAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Owners",
                columns: table => new
                {
                    Sub = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FamilyName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Owners", x => x.Sub);
                });

            migrationBuilder.CreateTable(
                name: "ReservedCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedCodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "LinkHits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LinkId = table.Column<int>(type: "int", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    Referer = table.Column<string>(type: "nvarchar(2048)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", nullable: true),
                    RemoteIpHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: true),
                    IsBot = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkHits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkHits_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkHits_AtUtc",
                table: "LinkHits",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LinkHits_LinkId",
                table: "LinkHits",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_Domain_Code",
                table: "Links",
                columns: new[] { "Domain", "Code" },
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Links_OwnerSub",
                table: "Links",
                column: "OwnerSub");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_Email",
                table: "Owners",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_ReservedCodes_Code",
                table: "ReservedCodes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkHits");

            migrationBuilder.DropTable(
                name: "Owners");

            migrationBuilder.DropTable(
                name: "ReservedCodes");

            migrationBuilder.DropTable(
                name: "Links");
        }
    }
}
