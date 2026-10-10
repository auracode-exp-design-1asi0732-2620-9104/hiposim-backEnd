using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HipoSim.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadsManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    simulation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    consent_granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads", x => x.id);
                    table.CheckConstraint("ck_leads_status", "status IN ('New', 'InContact', 'AppointmentScheduled', 'Closed', 'Discarded')");
                    table.ForeignKey(
                        name: "FK_leads_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leads_simulations_simulation_id",
                        column: x => x.simulation_id,
                        principalTable: "simulations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leads_users_buyer_id",
                        column: x => x.buyer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lead_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_lead_notes_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lead_notes_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lead_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    new_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_lead_status_changes_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lead_status_changes_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lead_notes_author_id",
                table: "lead_notes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_notes_lead_id",
                table: "lead_notes",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "IX_lead_status_changes_changed_by",
                table: "lead_status_changes",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_lead_status_changes_lead_id",
                table: "lead_status_changes",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_agency_id",
                table: "leads",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_buyer_id",
                table: "leads",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_simulation_id",
                table: "leads",
                column: "simulation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_notes");

            migrationBuilder.DropTable(
                name: "lead_status_changes");

            migrationBuilder.DropTable(
                name: "leads");
        }
    }
}
