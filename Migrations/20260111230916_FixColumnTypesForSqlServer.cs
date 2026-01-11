using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zazzo.ChoresWizard2000.Migrations
{
    /// <inheritdoc />
    public partial class FixColumnTypesForSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix string column types for SQL Server compatibility
            // nvarchar(max) columns cannot be sorted efficiently, so we specify max lengths
            // This only runs on SQL Server (skipped on SQLite)
            
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FamilyMembers')
                BEGIN
                    ALTER TABLE [FamilyMembers] ALTER COLUMN [Name] nvarchar(100) NOT NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Chores')
                BEGIN
                    ALTER TABLE [Chores] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
                    ALTER TABLE [Chores] ALTER COLUMN [Description] nvarchar(1000) NULL;
                    ALTER TABLE [Chores] ALTER COLUMN [Category] nvarchar(100) NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to nvarchar(max)
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FamilyMembers')
                BEGIN
                    ALTER TABLE [FamilyMembers] ALTER COLUMN [Name] nvarchar(max) NOT NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Chores')
                BEGIN
                    ALTER TABLE [Chores] ALTER COLUMN [Name] nvarchar(max) NOT NULL;
                    ALTER TABLE [Chores] ALTER COLUMN [Description] nvarchar(max) NULL;
                    ALTER TABLE [Chores] ALTER COLUMN [Category] nvarchar(max) NULL;
                END
            ");
        }
    }
}
