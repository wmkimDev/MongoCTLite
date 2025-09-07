using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using Testcontainers.MongoDb;

namespace MongoCTLite.Tests;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithPortBinding(27018, 27017)  
        .WithImage("mongo:7.0")
        .Build();

    public IMongoDatabase Db { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var client = new MongoClient(_container.GetConnectionString());
        Db = client.GetDatabase("testdb");
    }

    public async Task DisposeAsync()
    {
        // 환경변수 INSPECT_MONGO=1 이면 잠깐 붙잡아 둔다(기본 3분)
        if (Environment.GetEnvironmentVariable("INSPECT_MONGO") == "1")
        {
            Console.WriteLine("Holding Mongo container for inspection. Connect with Compass:");
            await Task.Delay(TimeSpan.FromMinutes(3));
        }

        await _container.DisposeAsync().AsTask();
    }
}