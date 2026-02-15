using System.Text;
using DIP.Backend.Data;
using DIP.Backend.Models;
using DIP.Backend.Models.Auth;
using DIP.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddControllers();

// Register DbContext with SQLite using connection string from configuration
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register repositories
builder.Services.AddScoped(typeof(DIP.Backend.Interfaces.IRepository<>), typeof(DIP.Backend.Repositories.Repository<>));
builder.Services.AddScoped<DIP.Backend.Interfaces.IUserRepository, DIP.Backend.Repositories.UserRepository>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(
            "http://localhost:5173"
            )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        );
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'"    
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme()
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference()
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }, new string[] {}
        }
    });
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<LiteratureApiKeysOptions>(builder.Configuration.GetSection(LiteratureApiKeysOptions.SectionName));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection(SmtpSettings.SectionName));
builder.Services.AddScoped<IPasswordHasher<DIP.Backend.Models.User>, PasswordHasher<DIP.Backend.Models.User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<DIP.Backend.Interfaces.IEmailService, SmtpEmailService>();


builder.Services.AddHttpClient<DIP.Backend.Services.Scrapers.SemanticScholarScraper>();
builder.Services.AddScoped<DIP.Backend.Interfaces.ILiteratureScraper, DIP.Backend.Services.Scrapers.SemanticScholarScraper>();

builder.Services.AddHttpClient<DIP.Backend.Services.Scrapers.IEEEXploreScraper>();
builder.Services.AddScoped<DIP.Backend.Interfaces.ILiteratureScraper, DIP.Backend.Services.Scrapers.IEEEXploreScraper>();

builder.Services.AddHttpClient<DIP.Backend.Services.Scrapers.ACMDigitalLibraryScraper>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseCookies = true,
        AllowAutoRedirect = true
    });
builder.Services.AddScoped<DIP.Backend.Interfaces.ILiteratureScraper, DIP.Backend.Services.Scrapers.ACMDigitalLibraryScraper>();

builder.Services.AddHttpClient<DIP.Backend.Services.LiteratureScraperService>();
builder.Services.AddScoped<DIP.Backend.Interfaces.ILiteratureScraperService, DIP.Backend.Services.LiteratureScraperService>();

var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["key"]));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = key,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DIP.Backend API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

/*app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");*/
app.MapControllers();

app.Run();
