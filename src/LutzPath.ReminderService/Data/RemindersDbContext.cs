using LutzPath.ReminderService.Models;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.ReminderService.Data;

public class RemindersDbContext : DbContext
{
    public RemindersDbContext(DbContextOptions<RemindersDbContext> options) : base(options)
    {
    }

    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reminder>().Property(entity => entity.DueDate)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        modelBuilder.Entity<Reminder>().HasIndex(reminder => reminder.UserId);
    }
}
