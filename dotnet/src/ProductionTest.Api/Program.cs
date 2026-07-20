var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Message = "Hello!",
    TimestampUtc = DateTimeOffset.UtcNow
}));

app.Run();
