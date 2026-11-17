using Microsoft.EntityFrameworkCore;

namespace Contoso.Academics;

public sealed class AcademicsDbContext(DbContextOptions<AcademicsDbContext> options) : DbContext(options)
{
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<DemoSeedHistory> DemoSeedHistory => Set<DemoSeedHistory>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Instructor>(entity =>
        {
            entity.Property(i => i.FirstName).HasMaxLength(50);
            entity.Property(i => i.LastName).HasMaxLength(50);
        });
        model.Entity<Department>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(50);
            entity.Property(d => d.Budget).HasColumnType("money");
            entity.HasOne(d => d.Instructor).WithMany().HasForeignKey(d => d.InstructorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Course>(entity =>
        {
            entity.Property(c => c.Title).HasMaxLength(50);
            entity.ToTable("Courses", table => table.HasCheckConstraint(
                "CK_Courses_Credits", "[Credits] >= 0 AND [Credits] <= 5"));
            entity.HasOne(c => c.Department).WithMany().HasForeignKey(c => c.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DemoSeedHistory>().Property(h => h.Id).HasMaxLength(100);
    }
}
