using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        "http://localhost:5010",
        "https://localhost:7106")
     .AllowAnyMethod()
     .AllowAnyHeader()));

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<MockPersonStore>();
builder.Services.AddSingleton<MockUserStore>();
builder.Services.AddSingleton<MockActivityStore>();
builder.Services.AddSingleton<MockAttachmentStore>();
builder.Services.AddSingleton<MockImageStore>();
builder.Services.AddSingleton<MockViewStore>();
builder.Services.AddSingleton<MockDropdownStore>();

var app = builder.Build();

app.UseCors();

app.MapPersonEndpoints();
app.MapUserEndpoints();
app.MapActivityEndpoints();
app.MapAttachmentEndpoints();
app.MapImageEndpoints();
app.MapViewEndpoints();
app.MapDropdownEndpoints();

app.Run();

public partial class Program { }
