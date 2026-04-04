using AI.DocumentAssistant.API;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Services.Storage;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data.Common;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private DbConnection _connection = default!;
    private string _storageRoot = default!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            _storageRoot = Path.Combine(Path.GetTempPath(), "ai-document-assistant-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageRoot);

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.RemoveAll(typeof(IOpenAiService));

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.AddScoped<IOpenAiService, FakeOpenAiService>();

            services.PostConfigure<LocalStorageOptions>(options =>
            {
                options.RootPath = _storageRoot;
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}