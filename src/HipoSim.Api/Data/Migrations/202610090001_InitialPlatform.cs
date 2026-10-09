using HipoSim.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HipoSim.Api.Data.Migrations;

[DbContext(typeof(HipoSimDbContext))]
[Migration("202610090001_InitialPlatform")]
public sealed class InitialPlatform : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agencies",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_agencies", x => x.id));

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                email_normalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                agency_id = table.Column<Guid>(type: "uuid", nullable: true),
                accepted_terms_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accepted_data_processing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
                table.CheckConstraint("ck_users_role", "role IN ('buyer', 'advisor')");
                table.ForeignKey("FK_users_agencies_agency_id", x => x.agency_id,
                    "agencies", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "simulations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                buyer_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                input_json = table.Column<string>(type: "jsonb", nullable: false),
                response_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_simulations", x => x.id);
                table.ForeignKey("FK_simulations_users_buyer_id", x => x.buyer_id,
                    "users", "id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("ix_users_email_normalized", "users", "email_normalized", unique: true);
        migrationBuilder.CreateIndex("ix_users_agency_id", "users", "agency_id");
        migrationBuilder.CreateIndex("ix_simulations_buyer_id", "simulations", "buyer_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("simulations");
        migrationBuilder.DropTable("users");
        migrationBuilder.DropTable("agencies");
    }
}
