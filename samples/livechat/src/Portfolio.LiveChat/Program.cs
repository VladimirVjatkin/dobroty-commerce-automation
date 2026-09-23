using Portfolio.LiveChat.Options;
using Portfolio.LiveChat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.SectionName));

// Typed HttpClient: the framework owns pooling/lifetime; ChatService receives a ready-to-use
// HttpClient through DI rather than constructing one itself.
builder.Services.AddHttpClient<IChatService, ChatService>();

var app = builder.Build();

// Serves the static frontend demo under wwwroot/ (index.html, css/, js/, vendor/). This is
// framework-provided middleware wiring, not application logic, so it is exercised by the live
// smoke test (see the sample README) rather than by a unit test.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
