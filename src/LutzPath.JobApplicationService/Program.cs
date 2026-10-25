using System.Text.Json.Serialization;
using LutzPath.JobApplicationService.Clients;
using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.Interfaces;
using LutzPath.JobApplicationService.Services;
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

builder.Services.AddDbContext<JobApplicationsDbContext>(options => options.UseSqlite(sqlite.ConnectionString));
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();

builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
{
    var baseUrl = builder.Configuration["Services:UserService"]
        ?? throw new InvalidOperationException("UserService URL is missing.");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

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
    var db = scope.ServiceProvider.GetRequiredService<JobApplicationsDbContext>();
    await db.Database.MigrateAsync();
}

app.MapControllers();
app.Run();
