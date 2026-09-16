using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests.Migrations;

[DbContext(typeof(EmulatorContext))]
[Migration("20260101000001_Owners")]
public class OwnersMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Owners",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Owners", x => x.Id));

        migrationBuilder.AddColumn<Guid>(
            name: "OwnerId",
            table: "Widgets",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Widgets_OwnerId",
            table: "Widgets",
            column: "OwnerId");

        migrationBuilder.AddForeignKey(
            name: "FK_Widgets_Owners_OwnerId",
            table: "Widgets",
            column: "OwnerId",
            principalTable: "Owners",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Widgets_Owners_OwnerId", table: "Widgets");
        migrationBuilder.DropIndex(name: "IX_Widgets_OwnerId", table: "Widgets");
        migrationBuilder.DropColumn(name: "OwnerId", table: "Widgets");
        migrationBuilder.DropTable(name: "Owners");
    }
}
