using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Survey_Certification_App.Data;
using Survey_Certification_App.IemailserviceInterface;
using Survey_Certification_App.Models;
using Survey_Certification_App.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Db
builder.Services.AddDbContext<ApplicationDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("dbconn")));


// ------------------------------------------------------
// 1) Controllers
// ------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Survey_Certification_App.Services.AccessScopeService>();
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstError = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e => new
                {
                    field = x.Key,
                    message = string.IsNullOrWhiteSpace(e.ErrorMessage)
                        ? "Invalid value."
                        : e.ErrorMessage
                }))
                .FirstOrDefault();

            return new BadRequestObjectResult(new
            {
                message = firstError?.message ?? "Validation failed",
                errors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .Select(x => new
                    {
                        field = x.Key,
                        errors = x.Value!.Errors.Select(e => e.ErrorMessage).ToList()
                    })
                    .ToList()
            });
        };
    })
    .AddJsonOptions(x => x.JsonSerializerOptions.PropertyNameCaseInsensitive = true);

// ------------------------------------------------------
// 2) JWT configuration  (MUST MATCH CENTRAL AUTH API)
// ------------------------------------------------------
var jwtKey = "THIS_SHOULD_BE_A_LONG_RANDOM_SECRET_KEY"; // same as CentralAuth
var jwtIssuer = "https://auth.mycompany.com";
var jwtAudience = "my-apps";

// Optional: bind JwtSettings if you need it somewhere else
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

// Add Authentication + JWT Bearer
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();



// ------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ------------------------------------------------------
// 5) Swagger with JWT support
// ------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter JWT as: Bearer {token}",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[]{}
        }
    });
});

// ------------------------------------------------------
// 6) Other services
// ------------------------------------------------------
builder.Services.AddSingleton<IhmMarkedImageQueue>();
builder.Services.AddSingleton<IIhmMarkedImageQueue>(sp =>
    sp.GetRequiredService<IhmMarkedImageQueue>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<IhmMarkedImageQueue>());
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('dbo.IhmCheckpointSubmissions', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.IhmCheckpointSubmissions
            (
                Id INT IDENTITY(1,1) NOT NULL
                    CONSTRAINT PK_IhmCheckpointSubmissions PRIMARY KEY,
                SurveyJobId INT NOT NULL,
                IhmCheckpointId INT NOT NULL,
                SurveyorUserId INT NOT NULL,
                DataJson NVARCHAR(MAX) NOT NULL,
                DataHash NVARCHAR(MAX) NOT NULL,
                Status NVARCHAR(MAX) NOT NULL,
                SubmittedAt DATETIME2 NOT NULL,
                ReviewedByUserId INT NULL,
                ReviewedAt DATETIME2 NULL,
                ReviewNotes NVARCHAR(MAX) NULL,
                CONSTRAINT FK_IhmCheckpointSubmissions_IhmCheckpoints_IhmCheckpointId
                    FOREIGN KEY (IhmCheckpointId)
                    REFERENCES dbo.IhmCheckpoints(Id) ON DELETE CASCADE,
                CONSTRAINT FK_IhmCheckpointSubmissions_SurveyJobs_SurveyJobId
                    FOREIGN KEY (SurveyJobId)
                    REFERENCES dbo.SurveyJobs(Id)
            );
            CREATE UNIQUE INDEX
                IX_IhmCheckpointSubmissions_IhmCheckpointId_SurveyorUserId
                ON dbo.IhmCheckpointSubmissions(IhmCheckpointId, SurveyorUserId);
            CREATE INDEX IX_IhmCheckpointSubmissions_SurveyJobId
                ON dbo.IhmCheckpointSubmissions(SurveyJobId);
        END

        IF OBJECT_ID('dbo.UserNotifications', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.UserNotifications
            (
                Id INT IDENTITY(1,1) NOT NULL
                    CONSTRAINT PK_UserNotifications PRIMARY KEY,
                UserId INT NOT NULL,
                Type NVARCHAR(50) NOT NULL,
                Title NVARCHAR(200) NOT NULL,
                Message NVARCHAR(1000) NOT NULL,
                SurveyJobId INT NULL,
                IsRead BIT NOT NULL
                    CONSTRAINT DF_UserNotifications_IsRead DEFAULT 0,
                CreatedAt DATETIME2 NOT NULL,
                ReadAt DATETIME2 NULL
            );
            CREATE INDEX IX_UserNotifications_UserId_IsRead_CreatedAt
                ON dbo.UserNotifications(UserId, IsRead, CreatedAt DESC);
        END

        IF OBJECT_ID('dbo.IhmImageMarkers', 'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH('dbo.IhmImageMarkers', 'Source') IS NULL
            BEGIN
                ALTER TABLE dbo.IhmImageMarkers
                    ADD [Source] NVARCHAR(100) NULL;
            END

            IF COL_LENGTH('dbo.IhmImageMarkers', 'SuppressRender') IS NULL
            BEGIN
                ALTER TABLE dbo.IhmImageMarkers
                    ADD [SuppressRender] BIT NOT NULL
                        CONSTRAINT DF_IhmImageMarkers_SuppressRender DEFAULT (0)
                        WITH VALUES;
            END

            IF COL_LENGTH('dbo.IhmImageMarkers', 'IsManualForExistingImage') IS NULL
            BEGIN
                ALTER TABLE dbo.IhmImageMarkers
                    ADD [IsManualForExistingImage] BIT NOT NULL
                        CONSTRAINT DF_IhmImageMarkers_IsManualForExistingImage DEFAULT (0)
                        WITH VALUES;
            END
        END
        """);
}

// ------------------------------------------------------
// 7) Middleware pipeline
// ------------------------------------------------------
app.UseSwagger();
app.UseSwaggerUI();

// Redirect root ("/") to swagger
app.MapGet("/", context =>
{
    context.Response.Redirect("/swagger/index.html");
    return Task.CompletedTask;
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

// CORS BEFORE auth
app.UseCors("AllowAll");
app.UseStaticFiles();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
