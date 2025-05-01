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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // District Relationships
            builder.Entity<District>()
                .HasMany(d => d.Schools)
                .WithOne(s => s.District)
                .HasForeignKey(s => s.DistrictId);

            builder.Entity<District>()
                .HasMany(d => d.Users)
                .WithOne(u => u.District)
                .HasForeignKey(u => u.DistrictId);

            // School Relationships
            builder.Entity<School>()
                .HasMany(s => s.Users)
                .WithOne(u => u.School)
                .HasForeignKey(u => u.SchoolId);

            // Default SQL values for District
            builder.Entity<District>()
                .Property(d => d.Inactive)
                .HasDefaultValue(false);

            builder.Entity<District>()
                .Property(d => d.DateCreated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<District>()
                .Property(d => d.DateUpdated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            // Default SQL values for School
            builder.Entity<School>()
                .Property(s => s.Inactive)
                .HasDefaultValue(false);

            builder.Entity<School>()
                .Property(s => s.DateCreated)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Entity<School>()
                .Property(s => s.DateUpdated)
                .HasDefaultValueSql("SYSUTCDATETIME()");
        }

    }

}
