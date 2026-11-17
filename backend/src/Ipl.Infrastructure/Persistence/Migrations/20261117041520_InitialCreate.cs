using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ipl");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserEmail = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetAllocations",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Competitions",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsHomeCountry = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsConfidential = table.Column<bool>(type: "bit", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessages",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    To = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceTransactions",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurseBalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReferenceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Franchises",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    City = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SecondaryColor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OwnerCompany = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OwnerEmail = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    HomeGround = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FoundedYear = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Franchises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginHistory",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageThreadReads",
                schema: "ipl",
                columns: table => new
                {
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageThreadReads", x => new { x.ThreadId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "MessageThreads",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartyFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TradeOfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                schema: "ipl",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    InApp = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => new { x.UserId, x.Type });
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerComparisons",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlayerIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsShared = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerComparisons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerRoles",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayingXIs",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Opponent = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Venue = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CaptainId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViceCaptainId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WicketKeeperId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsPreferred = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayingXIs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetentionPlans",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                schema: "ipl",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsActivated = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DefaultFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Team1 = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Team2 = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DataSource = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "ipl",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FranchiseStaff",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SinceYear = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseStaff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseStaff_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SenderFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_MessageThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalSchema: "ipl",
                        principalTable: "MessageThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    PhotoUrl = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BattingHand = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BowlingStyle = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BowlingType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PrimaryRoleId = table.Column<int>(type: "int", nullable: false),
                    SecondaryRoleId = table.Column<int>(type: "int", nullable: true),
                    IsWicketKeeper = table.Column<bool>(type: "bit", nullable: false),
                    IsOverseas = table.Column<bool>(type: "bit", nullable: false),
                    IsCapped = table.Column<bool>(type: "bit", nullable: false),
                    CurrentFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CareerStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "ipl",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Players_Franchises_CurrentFranchiseId",
                        column: x => x.CurrentFranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Players_PlayerRoles_PrimaryRoleId",
                        column: x => x.PrimaryRoleId,
                        principalSchema: "ipl",
                        principalTable: "PlayerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Players_PlayerRoles_SecondaryRoleId",
                        column: x => x.SecondaryRoleId,
                        principalSchema: "ipl",
                        principalTable: "PlayerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                schema: "ipl",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "ipl",
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ipl",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuctionEvents",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsMegaAuction = table.Column<bool>(type: "bit", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CurrentLotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BidTimerSeconds = table.Column<int>(type: "int", nullable: false),
                    AllowReopenUnsold = table.Column<bool>(type: "bit", nullable: false),
                    CurrentRound = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionEvents_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FranchiseSeasons",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    PurseTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurseRemaining = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OperatingBudget = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalPosition = table.Column<int>(type: "int", nullable: true),
                    Played = table.Column<int>(type: "int", nullable: false),
                    Won = table.Column<int>(type: "int", nullable: false),
                    Lost = table.Column<int>(type: "int", nullable: false),
                    NoResult = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    NetRunRate = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    IsChampion = table.Column<bool>(type: "bit", nullable: false),
                    IsRunnerUp = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseSeasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseSeasons_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FranchiseSeasons_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeasonRules",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonRules_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeOffers",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    InitiatorFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartyFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CashConsideration = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashPayerFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Conditions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AwaitingFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Franchises_CounterpartyFranchiseId",
                        column: x => x.CounterpartyFranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Franchises_InitiatorFranchiseId",
                        column: x => x.InitiatorFranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOffers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FranchiseMembers",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseMembers_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FranchiseMembers_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(88)", maxLength: 88, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReplacedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "ipl",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(88)", maxLength: 88, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuctionResults",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionResults_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuctionResults_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FranchiseSquads",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Acquisition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseSquads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseSquads_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FranchiseSquads_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FranchiseSquads_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAvailability",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PublicNote = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExpectedReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAvailability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAvailability_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerCareerStatistics",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    FirstYear = table.Column<int>(type: "int", nullable: false),
                    LastYear = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false),
                    Innings = table.Column<int>(type: "int", nullable: false),
                    NotOuts = table.Column<int>(type: "int", nullable: false),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    BallsFaced = table.Column<int>(type: "int", nullable: false),
                    HighestScore = table.Column<int>(type: "int", nullable: false),
                    HighestNotOut = table.Column<bool>(type: "bit", nullable: false),
                    Hundreds = table.Column<int>(type: "int", nullable: false),
                    Fifties = table.Column<int>(type: "int", nullable: false),
                    Fours = table.Column<int>(type: "int", nullable: false),
                    Sixes = table.Column<int>(type: "int", nullable: false),
                    BowlingInnings = table.Column<int>(type: "int", nullable: false),
                    BallsBowled = table.Column<int>(type: "int", nullable: false),
                    RunsConceded = table.Column<int>(type: "int", nullable: false),
                    Wickets = table.Column<int>(type: "int", nullable: false),
                    BestBowlingWickets = table.Column<int>(type: "int", nullable: false),
                    BestBowlingRuns = table.Column<int>(type: "int", nullable: false),
                    FourWicketHauls = table.Column<int>(type: "int", nullable: false),
                    FiveWicketHauls = table.Column<int>(type: "int", nullable: false),
                    Catches = table.Column<int>(type: "int", nullable: false),
                    Stumpings = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCareerStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCareerStatistics_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "ipl",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerCareerStatistics_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerContracts",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartSeasonId = table.Column<int>(type: "int", nullable: false),
                    EndSeasonId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AnnualSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Acquisition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerContracts_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerContracts_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMatchStatistics",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Team = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Opponent = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsHome = table.Column<bool>(type: "bit", nullable: false),
                    Batted = table.Column<bool>(type: "bit", nullable: false),
                    BattingPosition = table.Column<int>(type: "int", nullable: true),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    BallsFaced = table.Column<int>(type: "int", nullable: false),
                    Fours = table.Column<int>(type: "int", nullable: false),
                    Sixes = table.Column<int>(type: "int", nullable: false),
                    Dismissed = table.Column<bool>(type: "bit", nullable: false),
                    DismissalType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DismissedByBowlingType = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PowerplayRuns = table.Column<int>(type: "int", nullable: false),
                    PowerplayBalls = table.Column<int>(type: "int", nullable: false),
                    MiddleRuns = table.Column<int>(type: "int", nullable: false),
                    MiddleBalls = table.Column<int>(type: "int", nullable: false),
                    DeathRuns = table.Column<int>(type: "int", nullable: false),
                    DeathBalls = table.Column<int>(type: "int", nullable: false),
                    RunsVsPace = table.Column<int>(type: "int", nullable: false),
                    BallsVsPace = table.Column<int>(type: "int", nullable: false),
                    RunsVsSpin = table.Column<int>(type: "int", nullable: false),
                    BallsVsSpin = table.Column<int>(type: "int", nullable: false),
                    Bowled = table.Column<bool>(type: "bit", nullable: false),
                    BallsBowled = table.Column<int>(type: "int", nullable: false),
                    RunsConceded = table.Column<int>(type: "int", nullable: false),
                    Wickets = table.Column<int>(type: "int", nullable: false),
                    Maidens = table.Column<int>(type: "int", nullable: false),
                    DotBalls = table.Column<int>(type: "int", nullable: false),
                    PowerplayBallsBowled = table.Column<int>(type: "int", nullable: false),
                    PowerplayRunsConceded = table.Column<int>(type: "int", nullable: false),
                    PowerplayWickets = table.Column<int>(type: "int", nullable: false),
                    MiddleBallsBowled = table.Column<int>(type: "int", nullable: false),
                    MiddleRunsConceded = table.Column<int>(type: "int", nullable: false),
                    MiddleWickets = table.Column<int>(type: "int", nullable: false),
                    DeathBallsBowled = table.Column<int>(type: "int", nullable: false),
                    DeathRunsConceded = table.Column<int>(type: "int", nullable: false),
                    DeathWickets = table.Column<int>(type: "int", nullable: false),
                    WicketsVsRightHand = table.Column<int>(type: "int", nullable: false),
                    WicketsVsLeftHand = table.Column<int>(type: "int", nullable: false),
                    Catches = table.Column<int>(type: "int", nullable: false),
                    Stumpings = table.Column<int>(type: "int", nullable: false),
                    RunOuts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMatchStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatistics_Matches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "ipl",
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatistics_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMedicalRecords",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BodyPart = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Assessment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InjuryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    RecoveryProgress = table.Column<int>(type: "int", nullable: false),
                    FitnessCleared = table.Column<bool>(type: "bit", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClearedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMedicalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerMedicalRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerScoutingReports",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScoutUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchObserved = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Strengths = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weaknesses = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Observations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TechniqueRating = table.Column<int>(type: "int", nullable: false),
                    TemperamentRating = table.Column<int>(type: "int", nullable: false),
                    FitnessRating = table.Column<int>(type: "int", nullable: false),
                    PotentialRating = table.Column<int>(type: "int", nullable: false),
                    OverallRating = table.Column<int>(type: "int", nullable: false),
                    Recommendation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerScoutingReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerScoutingReports_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerScoutingReports_Users_ScoutUserId",
                        column: x => x.ScoutUserId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSeasonStatistics",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false),
                    Innings = table.Column<int>(type: "int", nullable: false),
                    NotOuts = table.Column<int>(type: "int", nullable: false),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    BallsFaced = table.Column<int>(type: "int", nullable: false),
                    HighestScore = table.Column<int>(type: "int", nullable: false),
                    HighestNotOut = table.Column<bool>(type: "bit", nullable: false),
                    Hundreds = table.Column<int>(type: "int", nullable: false),
                    Fifties = table.Column<int>(type: "int", nullable: false),
                    Fours = table.Column<int>(type: "int", nullable: false),
                    Sixes = table.Column<int>(type: "int", nullable: false),
                    BowlingInnings = table.Column<int>(type: "int", nullable: false),
                    BallsBowled = table.Column<int>(type: "int", nullable: false),
                    RunsConceded = table.Column<int>(type: "int", nullable: false),
                    Wickets = table.Column<int>(type: "int", nullable: false),
                    BestBowlingWickets = table.Column<int>(type: "int", nullable: false),
                    BestBowlingRuns = table.Column<int>(type: "int", nullable: false),
                    FourWicketHauls = table.Column<int>(type: "int", nullable: false),
                    FiveWicketHauls = table.Column<int>(type: "int", nullable: false),
                    Catches = table.Column<int>(type: "int", nullable: false),
                    Stumpings = table.Column<int>(type: "int", nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSeasonStatistics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSeasonStatistics_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "ipl",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerSeasonStatistics_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTeams",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Acquisition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalSchema: "ipl",
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerValuations",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    LowEstimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MidEstimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HighEstimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DemandIndex = table.Column<int>(type: "int", nullable: false),
                    LastKnownPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerValuations_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayingXIPlayers",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayingXIId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BattingOrder = table.Column<int>(type: "int", nullable: true),
                    IsImpactSubstitute = table.Column<bool>(type: "bit", nullable: false),
                    IsBowlingOption = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayingXIPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayingXIPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayingXIPlayers_PlayingXIs_PlayingXIId",
                        column: x => x.PlayingXIId,
                        principalSchema: "ipl",
                        principalTable: "PlayingXIs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseRecords",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RetentionRecords",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SlotNumber = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetentionRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetentionRecords_RetentionPlans_RetentionPlanId",
                        column: x => x.RetentionPlanId,
                        principalSchema: "ipl",
                        principalTable: "RetentionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScoutingAssignments",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScoutingAssignments_Users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalSchema: "ipl",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuctionPlayers",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SetNumber = table.Column<int>(type: "int", nullable: false),
                    LotNumber = table.Column<int>(type: "int", nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CurrentBid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrentBidFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastBidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionPlayers_AuctionEvents_AuctionEventId",
                        column: x => x.AuctionEventId,
                        principalSchema: "ipl",
                        principalTable: "AuctionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuctionPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeApprovals",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeOfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeApprovals_TradeOffers_TradeOfferId",
                        column: x => x.TradeOfferId,
                        principalSchema: "ipl",
                        principalTable: "TradeOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeNegotiations",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeOfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ByFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByUserName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    TermsSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeNegotiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeNegotiations_TradeOffers_TradeOfferId",
                        column: x => x.TradeOfferId,
                        principalSchema: "ipl",
                        principalTable: "TradeOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeOfferPlayers",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeOfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToFranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOfferPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeOfferPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeOfferPlayers_TradeOffers_TradeOfferId",
                        column: x => x.TradeOfferId,
                        principalSchema: "ipl",
                        principalTable: "TradeOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeTransactions",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeOfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeTransactions_TradeOffers_TradeOfferId",
                        column: x => x.TradeOfferId,
                        principalSchema: "ipl",
                        principalTable: "TradeOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractPayments",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinanceTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractPayments_PlayerContracts_PlayerContractId",
                        column: x => x.PlayerContractId,
                        principalSchema: "ipl",
                        principalTable: "PlayerContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuctionBids",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuctionPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlacedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionBids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionBids_AuctionPlayers_AuctionPlayerId",
                        column: x => x.AuctionPlayerId,
                        principalSchema: "ipl",
                        principalTable: "AuctionPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuctionBids_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionBids_AuctionPlayerId_PlacedAt",
                schema: "ipl",
                table: "AuctionBids",
                columns: new[] { "AuctionPlayerId", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionBids_FranchiseId_IdempotencyKey",
                schema: "ipl",
                table: "AuctionBids",
                columns: new[] { "FranchiseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionEvents_SeasonId",
                schema: "ipl",
                table: "AuctionEvents",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPlayers_AuctionEventId_PlayerId",
                schema: "ipl",
                table: "AuctionPlayers",
                columns: new[] { "AuctionEventId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPlayers_AuctionEventId_Status_SetNumber_LotNumber",
                schema: "ipl",
                table: "AuctionPlayers",
                columns: new[] { "AuctionEventId", "Status", "SetNumber", "LotNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPlayers_PlayerId",
                schema: "ipl",
                table: "AuctionPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionResults_AuctionEventId_FranchiseId",
                schema: "ipl",
                table: "AuctionResults",
                columns: new[] { "AuctionEventId", "FranchiseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionResults_AuctionPlayerId_Round",
                schema: "ipl",
                table: "AuctionResults",
                columns: new[] { "AuctionPlayerId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionResults_FranchiseId",
                schema: "ipl",
                table: "AuctionResults",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionResults_PlayerId",
                schema: "ipl",
                table: "AuctionResults",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                schema: "ipl",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                schema: "ipl",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_FranchiseId_At",
                schema: "ipl",
                table: "AuditLogs",
                columns: new[] { "FranchiseId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAllocations_FranchiseId_SeasonId_Category_Label",
                schema: "ipl",
                table: "BudgetAllocations",
                columns: new[] { "FranchiseId", "SeasonId", "Category", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Code",
                schema: "ipl",
                table: "Competitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractPayments_PlayerContractId",
                schema: "ipl",
                table: "ContractPayments",
                column: "PlayerContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Code",
                schema: "ipl",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerType_OwnerId",
                schema: "ipl",
                table: "Documents",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_SentAt",
                schema: "ipl",
                table: "EmailMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_FranchiseId_SeasonId_Category",
                schema: "ipl",
                table: "FinanceTransactions",
                columns: new[] { "FranchiseId", "SeasonId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_IdempotencyKey",
                schema: "ipl",
                table: "FinanceTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseMembers_FranchiseId_UserId",
                schema: "ipl",
                table: "FranchiseMembers",
                columns: new[] { "FranchiseId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseMembers_UserId",
                schema: "ipl",
                table: "FranchiseMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Franchises_Code",
                schema: "ipl",
                table: "Franchises",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseSeasons_FranchiseId_SeasonId",
                schema: "ipl",
                table: "FranchiseSeasons",
                columns: new[] { "FranchiseId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseSeasons_SeasonId",
                schema: "ipl",
                table: "FranchiseSeasons",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseSquads_FranchiseId_SeasonId_Status",
                schema: "ipl",
                table: "FranchiseSquads",
                columns: new[] { "FranchiseId", "SeasonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseSquads_PlayerId",
                schema: "ipl",
                table: "FranchiseSquads",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseSquads_SeasonId_PlayerId",
                schema: "ipl",
                table: "FranchiseSquads",
                columns: new[] { "SeasonId", "PlayerId" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseStaff_FranchiseId",
                schema: "ipl",
                table: "FranchiseStaff",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginHistory_UserId_At",
                schema: "ipl",
                table: "LoginHistory",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_CompetitionId_Year",
                schema: "ipl",
                table: "Matches",
                columns: new[] { "CompetitionId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ThreadId_SentAt",
                schema: "ipl",
                table: "Messages",
                columns: new[] { "ThreadId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreads_CounterpartyFranchiseId_LastMessageAt",
                schema: "ipl",
                table: "MessageThreads",
                columns: new[] { "CounterpartyFranchiseId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreads_FranchiseId_LastMessageAt",
                schema: "ipl",
                table: "MessageThreads",
                columns: new[] { "FranchiseId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DedupKey",
                schema: "ipl",
                table: "Notifications",
                columns: new[] { "UserId", "DedupKey" },
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt_CreatedAt",
                schema: "ipl",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                schema: "ipl",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAvailability_PlayerId",
                schema: "ipl",
                table: "PlayerAvailability",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCareerStatistics_CompetitionId",
                schema: "ipl",
                table: "PlayerCareerStatistics",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCareerStatistics_PlayerId_CompetitionId",
                schema: "ipl",
                table: "PlayerCareerStatistics",
                columns: new[] { "PlayerId", "CompetitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerContracts_FranchiseId",
                schema: "ipl",
                table: "PlayerContracts",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerContracts_PlayerId_Status",
                schema: "ipl",
                table: "PlayerContracts",
                columns: new[] { "PlayerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatistics_MatchId_PlayerId",
                schema: "ipl",
                table: "PlayerMatchStatistics",
                columns: new[] { "MatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatistics_PlayerId",
                schema: "ipl",
                table: "PlayerMatchStatistics",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMedicalRecords_PlayerId_InjuryDate",
                schema: "ipl",
                table: "PlayerMedicalRecords",
                columns: new[] { "PlayerId", "InjuryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRoles_Code",
                schema: "ipl",
                table: "PlayerRoles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Code",
                schema: "ipl",
                table: "Players",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_CountryId",
                schema: "ipl",
                table: "Players",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_CurrentFranchiseId",
                schema: "ipl",
                table: "Players",
                column: "CurrentFranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_FullName",
                schema: "ipl",
                table: "Players",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Players_PrimaryRoleId",
                schema: "ipl",
                table: "Players",
                column: "PrimaryRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_SecondaryRoleId",
                schema: "ipl",
                table: "Players",
                column: "SecondaryRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Status",
                schema: "ipl",
                table: "Players",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoutingReports_FranchiseId_PlayerId",
                schema: "ipl",
                table: "PlayerScoutingReports",
                columns: new[] { "FranchiseId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoutingReports_PlayerId",
                schema: "ipl",
                table: "PlayerScoutingReports",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerScoutingReports_ScoutUserId",
                schema: "ipl",
                table: "PlayerScoutingReports",
                column: "ScoutUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSeasonStatistics_CompetitionId",
                schema: "ipl",
                table: "PlayerSeasonStatistics",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSeasonStatistics_PlayerId_CompetitionId_Year",
                schema: "ipl",
                table: "PlayerSeasonStatistics",
                columns: new[] { "PlayerId", "CompetitionId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_FranchiseId",
                schema: "ipl",
                table: "PlayerTeams",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_PlayerId_SeasonId",
                schema: "ipl",
                table: "PlayerTeams",
                columns: new[] { "PlayerId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_SeasonId",
                schema: "ipl",
                table: "PlayerTeams",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerValuations_PlayerId_SeasonId_CreatedAt",
                schema: "ipl",
                table: "PlayerValuations",
                columns: new[] { "PlayerId", "SeasonId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayingXIPlayers_PlayerId",
                schema: "ipl",
                table: "PlayingXIPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayingXIPlayers_PlayingXIId",
                schema: "ipl",
                table: "PlayingXIPlayers",
                column: "PlayingXIId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "ipl",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                schema: "ipl",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseRecords_FranchiseId_SeasonId",
                schema: "ipl",
                table: "ReleaseRecords",
                columns: new[] { "FranchiseId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseRecords_PlayerId",
                schema: "ipl",
                table: "ReleaseRecords",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionRecords_FranchiseId_SeasonId_Status",
                schema: "ipl",
                table: "RetentionRecords",
                columns: new[] { "FranchiseId", "SeasonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionRecords_PlayerId",
                schema: "ipl",
                table: "RetentionRecords",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionRecords_RetentionPlanId",
                schema: "ipl",
                table: "RetentionRecords",
                column: "RetentionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                schema: "ipl",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Code",
                schema: "ipl",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_AssignedToId",
                schema: "ipl",
                table: "ScoutingAssignments",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutingAssignments_PlayerId",
                schema: "ipl",
                table: "ScoutingAssignments",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonRules_SeasonId_Key",
                schema: "ipl",
                table: "SeasonRules",
                columns: new[] { "SeasonId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_Year",
                schema: "ipl",
                table: "Seasons",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeApprovals_TradeOfferId",
                schema: "ipl",
                table: "TradeApprovals",
                column: "TradeOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeNegotiations_TradeOfferId",
                schema: "ipl",
                table: "TradeNegotiations",
                column: "TradeOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOfferPlayers_PlayerId",
                schema: "ipl",
                table: "TradeOfferPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOfferPlayers_TradeOfferId_PlayerId",
                schema: "ipl",
                table: "TradeOfferPlayers",
                columns: new[] { "TradeOfferId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_CounterpartyFranchiseId_Status",
                schema: "ipl",
                table: "TradeOffers",
                columns: new[] { "CounterpartyFranchiseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_InitiatorFranchiseId_Status",
                schema: "ipl",
                table: "TradeOffers",
                columns: new[] { "InitiatorFranchiseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_Reference",
                schema: "ipl",
                table: "TradeOffers",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_SeasonId",
                schema: "ipl",
                table: "TradeOffers",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeTransactions_TradeOfferId",
                schema: "ipl",
                table: "TradeTransactions",
                column: "TradeOfferId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_FranchiseId",
                schema: "ipl",
                table: "UserRoles",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "ipl",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId_FranchiseId",
                schema: "ipl",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId", "FranchiseId" },
                unique: true,
                filter: "[FranchiseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "ipl",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_TokenHash",
                schema: "ipl",
                table: "UserTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_UserId",
                schema: "ipl",
                table: "UserTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionBids",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "AuctionResults",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "BudgetAllocations",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "ContractPayments",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "EmailMessages",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "FinanceTransactions",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "FranchiseMembers",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "FranchiseSeasons",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "FranchiseSquads",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "FranchiseStaff",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "LoginHistory",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "MessageThreadReads",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerAvailability",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerCareerStatistics",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerComparisons",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerMatchStatistics",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerMedicalRecords",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerScoutingReports",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerSeasonStatistics",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerTeams",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerValuations",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayingXIPlayers",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "ReleaseRecords",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "RetentionRecords",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "RolePermissions",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "ScoutingAssignments",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "SeasonRules",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "SystemSettings",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "TradeApprovals",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "TradeNegotiations",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "TradeOfferPlayers",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "TradeTransactions",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "AuctionPlayers",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerContracts",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "MessageThreads",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Matches",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayingXIs",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "RetentionPlans",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "TradeOffers",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "AuctionEvents",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Players",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Competitions",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Seasons",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "Franchises",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PlayerRoles",
                schema: "ipl");
        }
    }
}
