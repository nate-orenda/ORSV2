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
        public DbSet<ORSV2.Models.Standard> Standards { get; set; }
        public DbSet<TargetGroup> TargetGroups { get; set; }
        public DbSet<TargetGroupStudent> TargetGroupStudents { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // STU (Students table)
            builder.Entity<STU>(entity =>
            {
                entity.HasKey(s => s.StuId);
                entity.ToTable("Students", t => t.ExcludeFromMigrations());
            });

            builder.Entity<Staff>(b =>
            {
                b.ToTable("Staff", t => t.ExcludeFromMigrations());
                b.HasKey(s => s.StaffId);
            });

            builder.Entity<Courses>(b =>
            {
                b.ToTable("Courses", t => t.ExcludeFromMigrations());
                b.HasKey(c => c.Id);
            });

            builder.Entity<GAAGProgress>().ToTable("GAAGProgress").HasNoKey();

            builder.Entity<GACheckpointSchedule>()
                .HasOne(s => s.School)
                .WithMany()
                .HasForeignKey(s => s.SchoolId);

            // GAResults: table name + PK + existing District FK
            builder.Entity<GAResults>(e =>
            {
                e.ToTable("GAResults", t => t.ExcludeFromMigrations());
                e.HasKey(r => r.ResultId);

                e.HasOne(r => r.District)
                 .WithMany()
                 .HasForeignKey(r => r.DistrictId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // TargetGroups
            builder.Entity<TargetGroup>(e =>
            {
                e.ToTable("TargetGroups", t => t.ExcludeFromMigrations());
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            });

            // TargetGroupStudents – composite key (TargetGroupId, StudentId)
            builder.Entity<TargetGroupStudent>(e =>
            {
                e.ToTable("TargetGroupStudents", t => t.ExcludeFromMigrations());
                e.HasKey(x => new { x.TargetGroupId, x.StudentId });

                e.HasOne(x => x.TargetGroup)
                    .WithMany(g => g.TargetGroupStudents)
                    .HasForeignKey(x => x.TargetGroupId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Student)
                    .WithMany()
                    .HasForeignKey(x => x.StudentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // District relationships
            builder.Entity<District>()
                .HasMany(d => d.Schools)
                .WithOne(s => s.District)
                .HasForeignKey(s => s.DistrictId);

            builder.Entity<District>()
                .HasMany(d => d.Users)
                .WithOne(u => u.District)
                .HasForeignKey(u => u.DistrictId);

            // User ↔ School (many-to-many)
            builder.Entity<UserSchool>()
                .HasKey(us => new { us.UserId, us.SchoolId });
            builder.Entity<UserSchool>()
                .HasOne(us => us.User).WithMany(u => u.UserSchools).HasForeignKey(us => us.UserId);
            builder.Entity<UserSchool>()
                .HasOne(us => us.School).WithMany().HasForeignKey(us => us.SchoolId);

            // Defaults
            builder.Entity<District>().Property(d => d.Inactive).HasDefaultValue(false);
            builder.Entity<District>().Property(d => d.DateCreated).HasDefaultValueSql("SYSUTCDATETIME()");
            builder.Entity<District>().Property(d => d.DateUpdated).HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<School>().Property(s => s.Inactive).HasDefaultValue(false);
            builder.Entity<School>().Property(s => s.DateCreated).HasDefaultValueSql("SYSUTCDATETIME()");
            builder.Entity<School>().Property(s => s.DateUpdated).HasDefaultValueSql("SYSUTCDATETIME()");

            // View
            builder.Entity<VwStudentResultsClasses>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("vw_student_results_classes");
            });

            // Assessments
            builder.Entity<Assessment>(entity =>
            {
                entity.ToTable("assessments");
                entity.HasKey(e => e.TestId);
                entity.Property(e => e.TestId).HasColumnName("test_id").HasMaxLength(255).IsRequired();
                entity.Property(e => e.DistrictId).HasColumnName("districtid").IsRequired();
                entity.Property(e => e.TestName).HasColumnName("test_name");
                entity.Property(e => e.Unit).HasColumnName("unit").IsRequired();
                entity.Property(e => e.Standards).HasColumnName("standards").IsRequired();
                entity.HasOne(a => a.District)
                      .WithMany()
                      .HasForeignKey(a => a.DistrictId)
                      .HasConstraintName("FK_assessments_districts");
            });
        }
    }
}
