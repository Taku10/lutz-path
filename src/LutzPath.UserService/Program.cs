using System.Text.Json.Serialization;
using LutzPath.UserService.Data;
using LutzPath.UserService.Interfaces;
using LutzPath.UserService.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

// Resolve relative database paths from this API's content root, not the terminal's directory.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("The database connection string is missing.");
var sqlite = new SqliteConnectionStringBuilder(connectionString);
if (!Path.IsPathRooted(sqlite.DataSource) && sqlite.DataSource != ":memory:")
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}

builder.Services.AddDbContext<UsersDbContext>(options => options.UseSqlite(sqlite.ConnectionString));
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Suitable for this single-instance learning project; apply this service's migrations only.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
    await db.Database.MigrateAsync();
}

app.MapControllers();
app.Run();
