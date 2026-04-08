using AI.DocumentAssistant.API;
using AI.DocumentAssistant.Application.Abstractions.AI;
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
    private DbConnection? _connection;
    private string? _storageRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        Environment.SetEnvironmentVariable("Jwt__Issuer", "AI.DocumentAssistant.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "AI.DocumentAssistant.Tests.Users");
        Environment.SetEnvironmentVariable("Jwt__SecretKey", "test-secret-key-123456789012345678901234567890");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenExpirationDays", "7");

        Environment.SetEnvironmentVariable("OpenAI__ApiKey", "test-key");
        Environment.SetEnvironmentVariable("OpenAI__Model", "gpt-4o-mini");
        Environment.SetEnvironmentVariable("OpenAI__BaseUrl", "https://api.openai.com/v1/");

        _storageRoot = Path.Combine(
            Path.GetTempPath(),
            "ai-document-assistant-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_storageRoot);

        Environment.SetEnvironmentVariable("LocalStorage__RootPath", _storageRoot);

        builder.ConfigureServices(services =>
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.RemoveAll(typeof(IOpenAiService));
            services.RemoveAll(typeof(IEmbeddingService));

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.AddScoped<IOpenAiService, FakeOpenAiService>();
            services.AddScoped<IEmbeddingService, FakeEmbeddingService>();

            var serviceProvider = services.BuildServiceProvider();

            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        if (!string.IsNullOrWhiteSpace(_storageRoot) && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }

        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", null);
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenExpirationDays", null);

        Environment.SetEnvironmentVariable("OpenAI__ApiKey", null);
        Environment.SetEnvironmentVariable("OpenAI__Model", null);
        Environment.SetEnvironmentVariable("OpenAI__BaseUrl", null);

        Environment.SetEnvironmentVariable("LocalStorage__RootPath", null);
    }
}