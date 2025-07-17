using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ORSV2.Models;

namespace ORSV2.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
        public DbSet<District> Districts { get; set; }
        public DbSet<School> Schools { get; set; }
        public DbSet<STU> STU => Set<STU>();
        public DbSet<Courses> Courses { get; set; }
        public DbSet<UserSchool> UserSchools { get; set; }
        public DbSet<Staff> Staff { get; set; }
        public DbSet<GACheckpointSchedule> GACheckpointSchedule { get; set; }
        public DbSet<GAResults> GAResults { get; set; }
        public DbSet<GAQuadrantIndicators> GAQuadrantIndicators { get; set; }
        public DbSet<StudentAttendance> StudentAttendance { get; set; }
        public DbSet<GAMatrix> GAMatrix { get; set; }
        public DbSet<GAAGProgress> GAAGProgress { get; set; }
        public DbSet<Grades> Grades { get; set; }
        public DbSet<StudentClass> StudentClasses { get; set; }
        public DbSet<MasterSchedule> MasterSchedule { get; set; }
        public DbSet<GAProtocol> GAProtocols { get; set; }
        public DbSet<GAProtocolSectionResponse> GAProtocolSectionResponses { get; set; }
        public DbSet<GAProtocolTarget> GAProtocolTargets { get; set; }
        public DbSet<GAProtocolActionPlanItem> GAProtocolActionPlanItems { get; set; }
        public DbSet<Assessment> Assessments { get; set; }

        public DbSet<VwStudentResultsClasses> VwStudentResultsClasses { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // STU primary key
            builder.Entity<STU>(entity =>
            {
                entity.HasKey(s => s.StuId);      // ✅ needed to enable LINQ and sorting
                entity.ToTable("Students", t => t.ExcludeFromMigrations());  // ✅ prevents EF from trying to create/update the table
            });

            builder.Entity<Staff>(builder =>
            {
                builder.ToTable("Staff", t => t.ExcludeFromMigrations()); // Prevent EF from generating this in migrations
                builder.HasKey(s => s.StaffId); // Only if there's a real primary key
            });

            builder.Entity<Courses>(builder =>
            {
                builder.ToTable("Courses", t => t.ExcludeFromMigrations()); // Prevent EF from generating this in migrations
                builder.HasKey(c => c.Id); // Only if there's a real primary key
            });

            builder.Entity<GAAGProgress>().ToTable("GAAGProgress").HasNoKey();

            builder.Entity<GACheckpointSchedule>()
            .HasOne(s => s.School)
            .WithMany() // or .WithMany(s => s.GACheckpointSchedules) if reverse nav exists
            .HasForeignKey(s => s.SchoolId);

            builder.Entity<GAResults>()
            .HasOne(r => r.District)
            .WithMany()
            .HasForeignKey(r => r.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

            // District relationships
            builder.Entity<District>()
                .HasMany(d => d.Schools)
                .WithOne(s => s.District)
                .HasForeignKey(s => s.DistrictId);

            builder.Entity<District>()
                .HasMany(d => d.Users)
                .WithOne(u => u.District)
                .HasForeignKey(u => u.DistrictId);

            // User ↔ School (many-to-many) via UserSchools
            builder.Entity<UserSchool>()
                .HasKey(us => new { us.UserId, us.SchoolId });

            builder.Entity<UserSchool>()
                .HasOne(us => us.User)
                .WithMany(u => u.UserSchools)
                .HasForeignKey(us => us.UserId);

            builder.Entity<UserSchool>()
                .HasOne(us => us.School)
                .WithMany()
                .HasForeignKey(us => us.SchoolId);

            // Defaults for District
            builder.Entity<District>()
                .Property(d => d.Inactive)
                .HasDefaultValue(false);

            builder.Entity<District>()
                .Property(d => d.DateCreated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<District>()
                .Property(d => d.DateUpdated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            // Defaults for School
            builder.Entity<School>()
                .Property(s => s.Inactive)
                .HasDefaultValue(false);

            builder.Entity<School>()
                .Property(s => s.DateCreated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<School>()
                .Property(s => s.DateUpdated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<VwStudentResultsClasses>(entity =>
            {
                entity.HasNoKey(); // Views typically don't have primary keys
                entity.ToView("vw_student_results_classes"); // Map to the actual view name
            });

            builder.Entity<Assessment>(entity =>
            {
                entity.ToTable("assessments");

                entity.HasKey(e => e.TestId);

                entity.Property(e => e.TestId)
                    .HasColumnName("test_id")
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.DistrictId)
                    .HasColumnName("districtid")
                    .IsRequired();

                entity.Property(e => e.TestName)
                    .HasColumnName("test_name");

                entity.Property(e => e.Unit)
                    .HasColumnName("unit")
                    .IsRequired();

                entity.Property(e => e.Standards)
                    .HasColumnName("standards")
                    .IsRequired();

                // Foreign key relationship
                entity.HasOne(a => a.District)
                    .WithMany()
                    .HasForeignKey(a => a.DistrictId)
                    .HasConstraintName("FK_assessments_districts");
            });
        }
    }
}
