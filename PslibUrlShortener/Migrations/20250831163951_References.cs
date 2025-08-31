using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PslibUrlShortener.Migrations
{
    /// <inheritdoc />
    public partial class References : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "Links");

            migrationBuilder.AddColumn<int>(
                name: "LinkId1",
                table: "LinkHits",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkHits_LinkId1",
                table: "LinkHits",
                column: "LinkId1");

            migrationBuilder.AddForeignKey(
                name: "FK_LinkHits_Links_LinkId1",
                table: "LinkHits",
                column: "LinkId1",
                principalTable: "Links",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Links_Owners_OwnerSub",
                table: "Links",
                column: "OwnerSub",
                principalTable: "Owners",
                principalColumn: "Sub",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LinkHits_Links_LinkId1",
                table: "LinkHits");

            migrationBuilder.DropForeignKey(
                name: "FK_Links_Owners_OwnerSub",
                table: "Links");

            migrationBuilder.DropIndex(
                name: "IX_LinkHits_LinkId1",
                table: "LinkHits");

            migrationBuilder.DropColumn(
                name: "LinkId1",
                table: "LinkHits");

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "Links",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }
    }
}
