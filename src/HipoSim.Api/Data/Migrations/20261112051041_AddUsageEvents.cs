using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HipoSim.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    properties = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_events", x => x.id);
                    table.CheckConstraint("ck_usage_events_source", "source IN ('landing', 'mobile', 'web')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_name_occurred_at",
                table: "usage_events",
                columns: new[] { "name", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_occurred_at",
                table: "usage_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_session_id",
                table: "usage_events",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_events");
        }
    }
}
