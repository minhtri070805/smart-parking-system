using Microsoft.EntityFrameworkCore;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Persistence.Data;

public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options)
{
    public DbSet<CashHandover> CashHandovers => Set<CashHandover>();
    public DbSet<ParkingShift> ParkingShifts => Set<ParkingShift>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<CashReconciliation> CashReconciliations => Set<CashReconciliation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("operations");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OperationsDbContext).Assembly);
    }
}
