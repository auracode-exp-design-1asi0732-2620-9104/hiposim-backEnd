using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Data;

public sealed class HipoSimDbContext(DbContextOptions<HipoSimDbContext> options) : DbContext(options)
{
    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StoredSimulation> Simulations => Set<StoredSimulation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agency>(entity =>
        {
            entity.ToTable("agencies");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasColumnName("id");
            entity.Property(a => a.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            entity.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
            entity.Property(u => u.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            entity.Property(u => u.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(254).IsRequired();
            entity.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
            entity.Property(u => u.Role).HasColumnName("role").HasMaxLength(30).IsRequired();
            entity.Property(u => u.AgencyId).HasColumnName("agency_id");
            entity.Property(u => u.AcceptedTermsAt).HasColumnName("accepted_terms_at").IsRequired();
            entity.Property(u => u.AcceptedDataProcessingAt).HasColumnName("accepted_data_processing_at").IsRequired();
            entity.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(u => u.EmailNormalized).IsUnique().HasDatabaseName("ix_users_email_normalized");
            entity.HasIndex(u => u.AgencyId).HasDatabaseName("ix_users_agency_id");
            entity.HasOne<Agency>().WithMany().HasForeignKey(u => u.AgencyId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("ck_users_role", "role IN ('buyer', 'advisor')"));
        });

        modelBuilder.Entity<StoredSimulation>(entity =>
        {
            entity.ToTable("simulations");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.BuyerId).HasColumnName("buyer_id");
            entity.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(s => s.InputJson).HasColumnName("input_json").HasColumnType("jsonb").IsRequired();
            entity.Property(s => s.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb").IsRequired();
            entity.HasIndex(s => s.BuyerId).HasDatabaseName("ix_simulations_buyer_id");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(s => s.BuyerId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
