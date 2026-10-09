using HipoSim.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace HipoSim.Api.Data.Migrations;

[DbContext(typeof(HipoSimDbContext))]
public sealed class HipoSimDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.10");

        modelBuilder.Entity("HipoSim.Api.Data.Agency", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("Name").IsRequired().HasMaxLength(160)
                .HasColumnType("character varying(160)").HasColumnName("name");
            entity.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.HasKey("Id");
            entity.ToTable("agencies");
        });

        modelBuilder.Entity("HipoSim.Api.Data.AppUser", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<string>("FullName").IsRequired().HasMaxLength(150)
                .HasColumnType("character varying(150)").HasColumnName("full_name");
            entity.Property<string>("Email").IsRequired().HasMaxLength(254)
                .HasColumnType("character varying(254)").HasColumnName("email");
            entity.Property<string>("EmailNormalized").IsRequired().HasMaxLength(254)
                .HasColumnType("character varying(254)").HasColumnName("email_normalized");
            entity.Property<string>("Phone").IsRequired().HasMaxLength(20)
                .HasColumnType("character varying(20)").HasColumnName("phone");
            entity.Property<string>("PasswordHash").IsRequired().HasMaxLength(512)
                .HasColumnType("character varying(512)").HasColumnName("password_hash");
            entity.Property<string>("Role").IsRequired().HasMaxLength(30)
                .HasColumnType("character varying(30)").HasColumnName("role");
            entity.Property<Guid?>("AgencyId").HasColumnType("uuid").HasColumnName("agency_id");
            entity.Property<DateTimeOffset>("AcceptedTermsAt")
                .HasColumnType("timestamp with time zone").HasColumnName("accepted_terms_at");
            entity.Property<DateTimeOffset>("AcceptedDataProcessingAt")
                .HasColumnType("timestamp with time zone").HasColumnName("accepted_data_processing_at");
            entity.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.HasKey("Id");
            entity.HasIndex("EmailNormalized").IsUnique().HasDatabaseName("ix_users_email_normalized");
            entity.HasIndex("AgencyId").HasDatabaseName("ix_users_agency_id");
            entity.ToTable("users", table => table.HasCheckConstraint("ck_users_role", "role IN ('buyer', 'advisor')"));
        });

        modelBuilder.Entity("HipoSim.Api.Data.StoredSimulation", entity =>
        {
            entity.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            entity.Property<Guid?>("BuyerId").HasColumnType("uuid").HasColumnName("buyer_id");
            entity.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property<string>("InputJson").IsRequired().HasColumnType("jsonb").HasColumnName("input_json");
            entity.Property<string>("ResponseJson").IsRequired().HasColumnType("jsonb").HasColumnName("response_json");
            entity.HasKey("Id");
            entity.HasIndex("BuyerId").HasDatabaseName("ix_simulations_buyer_id");
            entity.ToTable("simulations");
        });

        modelBuilder.Entity("HipoSim.Api.Data.AppUser", entity =>
        {
            entity.HasOne("HipoSim.Api.Data.Agency", null)
                .WithMany().HasForeignKey("AgencyId").OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity("HipoSim.Api.Data.StoredSimulation", entity =>
        {
            entity.HasOne("HipoSim.Api.Data.AppUser", null)
                .WithMany().HasForeignKey("BuyerId").OnDelete(DeleteBehavior.SetNull);
        });
    }
}
