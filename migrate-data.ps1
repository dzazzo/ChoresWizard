# PowerShell script to migrate local SQLite data to Azure SQL
# ============================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$AzureSqlConnectionString
)

$ErrorActionPreference = "Stop"

Write-Host "🧙 Data Migration Script - SQLite to Azure SQL" -ForegroundColor Magenta
Write-Host "=================================================" -ForegroundColor Magenta

# Check if SQLite database exists
$sqliteDb = "chores.db"
if (-not (Test-Path $sqliteDb)) {
    Write-Host "❌ Local SQLite database not found at $sqliteDb" -ForegroundColor Red
    exit 1
}

Write-Host "`n📊 Exporting data from SQLite..." -ForegroundColor Yellow

# Create a temporary .NET console app to perform the migration
$migrationCode = @'
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

class DataMigrator
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: DataMigrator <sqliteConnectionString> <azureSqlConnectionString>");
            return;
        }

        var sqliteConnStr = args[0];
        var azureSqlConnStr = args[1];

        Console.WriteLine("Starting data migration...");

        using var sqliteConn = new SqliteConnection(sqliteConnStr);
        using var sqlConn = new SqlConnection(azureSqlConnStr);

        sqliteConn.Open();
        sqlConn.Open();

        // Migrate FamilyMembers
        MigrateTable(sqliteConn, sqlConn, "FamilyMembers", 
            "INSERT INTO FamilyMembers (Id, Name, AgeGroup) VALUES (@Id, @Name, @AgeGroup)",
            reader => new Dictionary<string, object>
            {
                ["@Id"] = reader["Id"],
                ["@Name"] = reader["Name"],
                ["@AgeGroup"] = reader["AgeGroup"]
            });

        // Migrate Chores
        MigrateTable(sqliteConn, sqlConn, "Chores",
            "INSERT INTO Chores (Id, Name, Description, Frequency, AgeRestriction, AssignTo, IsActive) VALUES (@Id, @Name, @Description, @Frequency, @AgeRestriction, @AssignTo, @IsActive)",
            reader => new Dictionary<string, object>
            {
                ["@Id"] = reader["Id"],
                ["@Name"] = reader["Name"],
                ["@Description"] = reader["Description"] ?? DBNull.Value,
                ["@Frequency"] = reader["Frequency"],
                ["@AgeRestriction"] = reader["AgeRestriction"],
                ["@AssignTo"] = reader["AssignTo"],
                ["@IsActive"] = reader["IsActive"]
            });

        // Migrate ChoreAssignments
        MigrateTable(sqliteConn, sqlConn, "ChoreAssignments",
            "SET IDENTITY_INSERT ChoreAssignments ON; INSERT INTO ChoreAssignments (Id, ChoreId, FamilyMemberId, Month, Year, IsCompleted, CompletedDate) VALUES (@Id, @ChoreId, @FamilyMemberId, @Month, @Year, @IsCompleted, @CompletedDate); SET IDENTITY_INSERT ChoreAssignments OFF;",
            reader => new Dictionary<string, object>
            {
                ["@Id"] = reader["Id"],
                ["@ChoreId"] = reader["ChoreId"],
                ["@FamilyMemberId"] = reader["FamilyMemberId"],
                ["@Month"] = reader["Month"],
                ["@Year"] = reader["Year"],
                ["@IsCompleted"] = reader["IsCompleted"],
                ["@CompletedDate"] = reader["CompletedDate"] ?? DBNull.Value
            });

        Console.WriteLine("✓ Migration completed successfully!");
    }

    static void MigrateTable(SqliteConnection sqliteConn, SqlConnection sqlConn, string tableName,
        string insertSql, Func<SqliteDataReader, Dictionary<string, object>> paramMapper)
    {
        Console.WriteLine($"  Migrating {tableName}...");
        
        using var selectCmd = sqliteConn.CreateCommand();
        selectCmd.CommandText = $"SELECT * FROM {tableName}";
        
        using var reader = selectCmd.ExecuteReader();
        int count = 0;
        
        while (reader.Read())
        {
            try
            {
                using var insertCmd = sqlConn.CreateCommand();
                insertCmd.CommandText = insertSql;
                
                foreach (var param in paramMapper(reader))
                {
                    insertCmd.Parameters.AddWithValue(param.Key, param.Value);
                }
                
                insertCmd.ExecuteNonQuery();
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Warning: Could not insert row - {ex.Message}");
            }
        }
        
        Console.WriteLine($"    ✓ Migrated {count} rows from {tableName}");
    }
}
'@

Write-Host "`n🔧 To migrate your data, you have two options:" -ForegroundColor Cyan
Write-Host ""
Write-Host "OPTION 1: Use Entity Framework (Recommended)" -ForegroundColor Green
Write-Host "---------------------------------------------"
Write-Host @"
1. First, ensure the Azure SQL database has the schema by visiting your app URL once
   (the app runs migrations on startup in Production mode)

2. Then run this EF command to generate a migration script:
   dotnet ef migrations script --idempotent -o migration.sql

3. Connect to Azure SQL using Azure Data Studio or SSMS and run the script

4. Export your local data manually using SQLite tools and import to Azure SQL
"@

Write-Host ""
Write-Host "OPTION 2: Quick Data Export (for small datasets)" -ForegroundColor Green  
Write-Host "------------------------------------------------"
Write-Host @"

# Install the dotnet-ef tool if not already installed
dotnet tool install --global dotnet-ef

# View your local data
sqlite3 chores.db ".dump FamilyMembers"
sqlite3 chores.db ".dump Chores"  
sqlite3 chores.db ".dump ChoreAssignments"

# Then manually insert the data into Azure SQL using Azure Portal Query Editor
# or Azure Data Studio
"@

Write-Host ""
Write-Host "Your Azure SQL Connection String:" -ForegroundColor Yellow
Write-Host $AzureSqlConnectionString -ForegroundColor Gray
