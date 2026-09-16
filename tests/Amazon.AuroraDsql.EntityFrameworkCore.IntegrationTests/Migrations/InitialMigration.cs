using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests.Migrations;

[DbContext(typeof(EmulatorContext))]
[Migration("20260101000000_Initial")]
public class InitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Widgets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                Numbers = table.Column<string>(type: "jsonb", nullable: false),
                Tags = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Widgets", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_Widgets_Name",
            table: "Widgets",
            column: "Name");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Widgets");

        migrationBuilder.DropIndex(name: "IX_Widgets_Name", table: "Widgets");
    }
}
