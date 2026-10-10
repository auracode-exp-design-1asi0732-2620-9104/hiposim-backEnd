using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Data;

public sealed class HipoSimDbContext(DbContextOptions<HipoSimDbContext> options) : DbContext(options)
{
    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StoredSimulation> Simulations => Set<StoredSimulation>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadNote> LeadNotes => Set<LeadNote>();
    public DbSet<LeadStatusChange> LeadStatusChanges => Set<LeadStatusChange>();

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

        modelBuilder.Entity<Lead>(entity =>
        {
                entity.ToTable("leads");
                entity.HasKey(l => l.Id);

                entity.Property(l => l.Id).HasColumnName("id");
                entity.Property(l => l.BuyerId).HasColumnName("buyer_id").IsRequired();
                entity.Property(l => l.AgencyId).HasColumnName("agency_id").IsRequired();
                entity.Property(l => l.SimulationId).HasColumnName("simulation_id").IsRequired();
                entity.Property(l => l.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
                entity.Property(l => l.ConsentGrantedAt).HasColumnName("consent_granted_at").IsRequired();
                entity.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();

                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(l => l.BuyerId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Agency>()
                    .WithMany()
                    .HasForeignKey(l => l.AgencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<StoredSimulation>()
                    .WithMany()
                    .HasForeignKey(l => l.SimulationId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(l => l.AgencyId).HasDatabaseName("ix_leads_agency_id");
                entity.HasIndex(l => l.BuyerId).HasDatabaseName("ix_leads_buyer_id");
                entity.HasIndex(l => l.SimulationId).HasDatabaseName("ix_leads_simulation_id");

                entity.ToTable(table => table.HasCheckConstraint(
                    "ck_leads_status",
                    "status IN ('New', 'InContact', 'AppointmentScheduled', 'Closed', 'Discarded')"));
        });

        modelBuilder.Entity<LeadNote>(entity =>
        {
            entity.ToTable("lead_notes");
            entity.HasKey(n => n.Id);

            entity.Property(n => n.Id).HasColumnName("id");
            entity.Property(n => n.LeadId).HasColumnName("lead_id").IsRequired();
            entity.Property(n => n.AuthorId).HasColumnName("author_id").IsRequired();
            entity.Property(n => n.Content).HasColumnName("content").HasMaxLength(2000).IsRequired();
            entity.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasOne<Lead>()
                    .WithMany()
                    .HasForeignKey(n => n.LeadId)
                    .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(n => n.AuthorId)
                    .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasIndex(n => n.LeadId).HasDatabaseName("ix_lead_notes_lead_id");
        });

        modelBuilder.Entity<LeadStatusChange>(entity =>
        {
                entity.ToTable("lead_status_changes");
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Id).HasColumnName("id");
                entity.Property(c => c.LeadId).HasColumnName("lead_id").IsRequired();
                entity.Property(c => c.ChangedBy).HasColumnName("changed_by").IsRequired();
                entity.Property(c => c.PreviousStatus).HasColumnName("previous_status").HasMaxLength(30).IsRequired();
                entity.Property(c => c.NewStatus).HasColumnName("new_status").HasMaxLength(30).IsRequired();
                entity.Property(c => c.ChangedAt).HasColumnName("changed_at").IsRequired();

                entity.HasOne<Lead>()
                    .WithMany()
                    .HasForeignKey(c => c.LeadId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<AppUser>()
                    .WithMany()
                    .HasForeignKey(c => c.ChangedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => c.LeadId).HasDatabaseName("ix_lead_status_changes_lead_id");
        });
    }
}
