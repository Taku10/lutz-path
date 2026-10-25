using LutzPath.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.UserService.Data;

public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

}
