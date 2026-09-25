using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Paymob_Integration_Demo.Options;
using Paymob_Integration_Demo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PaymobOptions>(builder.Configuration.GetSection(PaymobOptions.SectionName));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddHttpClient<IPaymobService, PaymobService>((serviceProvider, client) =>
{
    var paymobOptions = serviceProvider.GetRequiredService<IOptions<PaymobOptions>>().Value;
    client.BaseAddress = new Uri(paymobOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(paymobOptions.TimeoutSeconds);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin();
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtKey = builder.Configuration["JwtSettings:Key"];
    var jwtIssuer = builder.Configuration["JwtSettings:Issuer"];
    var jwtAudience = builder.Configuration["JwtSettings:Audience"];

    if (string.IsNullOrWhiteSpace(jwtKey))
    {
        throw new InvalidOperationException("JwtSettings:Key is not configured.");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
        ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exception = context.Features.Get<IExceptionHandlerFeature>();
        var payload = new { message = exception?.Error.Message ?? "An unexpected error occurred." };

        await context.Response.WriteAsJsonAsync(payload);
    });
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { status = "ok", message = "Paymob Integration Demo API is ready." }));

app.Run();
