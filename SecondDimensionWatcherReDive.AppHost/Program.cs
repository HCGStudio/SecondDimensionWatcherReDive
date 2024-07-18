using SecondDimensionWatcherReDive.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var postgresDb = postgres.AddDatabase("sdw");

var backend = builder
    .AddProject<Projects.SecondDimensionWatcherReDive>("backend")
    .WithReference(postgresDb)
    .WaitFor(postgresDb);



var frontend = builder
    .AddYarnApp("frontend", "../SecondDimensionWatcherReDive.Client")
    .WithReference(backend)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

var gateway = builder
    .AddProject<Projects.SecondDimensionWatcherReDive_Gateway>("gateway")
    .WithReference(backend)
    .WithReference(frontend);

builder.Build().Run();