using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contoso.Academics;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AcademicsDbContext>
{
    public AcademicsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AcademicsDbContext>()
            .UseSqlServer("Server=localhost;Database=ContosoAcademicsDesign;Integrated Security=true;Encrypt=true;TrustServerCertificate=false")
            .Options);
}
