using System;
using System.Threading.Tasks;
using Npgsql;
using Serilog;


namespace CricData
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            configureLogger();

            var host = "localhost";
            var port = 5432;
            var username = "postgres";
            var password = "yourpassword";
            var database = "cricket_master_db";

            var masterConnection =
                $"Host={host};Port={port};Username={username};Password={password};Database=postgres";

            var appConnection =
                $"Host={host};Port={port};Username={username};Password={password};Database={database}";

            try
            {
                await EnsureDatabaseExists(masterConnection, database);
                await CreateSchema(appConnection);
            }
            catch (Exception ex)
            {
                Log.Information(ex.Message);
            }
        }

        private static void configureLogger()
        {
            Log.Logger = new LoggerConfiguration()
                       .MinimumLevel.Information()
                       .WriteTo.Console()
                       .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                       .CreateLogger();
        }

        static async Task EnsureDatabaseExists(string masterConnection, string databaseName)
        {
            await using var conn = new NpgsqlConnection(masterConnection);
            await conn.OpenAsync();

            var check = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @dbname", conn);

            check.Parameters.AddWithValue("dbname", databaseName);

            var exists = await check.ExecuteScalarAsync();

            if (exists == null)
            {
                Log.Information($"Creating database '{databaseName}'...");
                await new NpgsqlCommand(
                    $"CREATE DATABASE \"{databaseName}\"", conn)
                    .ExecuteNonQueryAsync();
            }
        }

        static async Task CreateSchema(string connectionString)
        {
            var sql = @"

CREATE TABLE IF NOT EXISTS teams (
    id BIGSERIAL PRIMARY KEY,
    name VARCHAR(100) UNIQUE NOT NULL,
    team_type VARCHAR(50)
);

CREATE TABLE IF NOT EXISTS players (
    id BIGSERIAL PRIMARY KEY,
    registry_id VARCHAR(20) UNIQUE,
    full_name VARCHAR(150) NOT NULL
);

CREATE TABLE IF NOT EXISTS venues (
    id BIGSERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    city VARCHAR(100),
    UNIQUE(name, city)
);

CREATE TABLE IF NOT EXISTS events (
    id BIGSERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    season VARCHAR(20),
    event_group VARCHAR(20),
    UNIQUE(name, season, event_group)
);

CREATE TABLE IF NOT EXISTS matches (
    id BIGSERIAL PRIMARY KEY,
    event_id BIGINT REFERENCES events(id),
    venue_id BIGINT REFERENCES venues(id),
    match_number INT,
    match_type VARCHAR(20),
    match_type_number INT,
    gender VARCHAR(20),
    overs_limit INT NULL,
    balls_per_over INT,
    toss_winner_team_id BIGINT REFERENCES teams(id),
    toss_decision VARCHAR(10),
    winner_team_id BIGINT REFERENCES teams(id),
    win_type VARCHAR(20),
    win_margin INT,
    outcome_method VARCHAR(20),   -- NEW
    player_of_match_id BIGINT REFERENCES players(id),
    UNIQUE(event_id, match_number, venue_id)
);

CREATE TABLE IF NOT EXISTS match_dates (
    match_id BIGINT REFERENCES matches(id) ON DELETE CASCADE,
    match_date DATE,
    PRIMARY KEY (match_id, match_date)
);

CREATE TABLE IF NOT EXISTS match_teams (
    match_id BIGINT REFERENCES matches(id) ON DELETE CASCADE,
    team_id BIGINT REFERENCES teams(id),
    PRIMARY KEY (match_id, team_id)
);

CREATE TABLE IF NOT EXISTS match_team_players (
    match_id BIGINT REFERENCES matches(id) ON DELETE CASCADE,
    team_id BIGINT REFERENCES teams(id),
    player_id BIGINT REFERENCES players(id),
    PRIMARY KEY (match_id, player_id)
);

CREATE TABLE IF NOT EXISTS officials (
    id BIGSERIAL PRIMARY KEY,
    player_id BIGINT REFERENCES players(id) UNIQUE
);

CREATE TABLE IF NOT EXISTS match_officials (
    match_id BIGINT REFERENCES matches(id) ON DELETE CASCADE,
    official_id BIGINT REFERENCES officials(id),
    role VARCHAR(30),
    PRIMARY KEY (match_id, official_id, role)
);

CREATE TABLE IF NOT EXISTS innings (
    id BIGSERIAL PRIMARY KEY,
    match_id BIGINT REFERENCES matches(id) ON DELETE CASCADE,
    team_id BIGINT REFERENCES teams(id),
    innings_no INT,
    penalty_runs_pre INT DEFAULT 0,
    penalty_runs_post INT DEFAULT 0,
    UNIQUE(match_id, innings_no)
);

CREATE TABLE IF NOT EXISTS overs (
    id BIGSERIAL PRIMARY KEY,
    innings_id BIGINT REFERENCES innings(id) ON DELETE CASCADE,
    over_no INT,
    UNIQUE(innings_id, over_no)
);

CREATE TABLE IF NOT EXISTS deliveries (
    id BIGSERIAL PRIMARY KEY,
    over_id BIGINT REFERENCES overs(id) ON DELETE CASCADE,
    ball_no INT,
    batter_id BIGINT REFERENCES players(id),
    bowler_id BIGINT REFERENCES players(id),
    non_striker_id BIGINT REFERENCES players(id),
    runs_batter INT DEFAULT 0,
    runs_extras INT DEFAULT 0,
    runs_total INT DEFAULT 0,
    CHECK (runs_total >= 0),
    UNIQUE(over_id, ball_no)
);

CREATE TABLE IF NOT EXISTS delivery_extras (
    delivery_id BIGINT PRIMARY KEY REFERENCES deliveries(id) ON DELETE CASCADE,
    wides INT DEFAULT 0,
    noballs INT DEFAULT 0,
    byes INT DEFAULT 0,
    legbyes INT DEFAULT 0
);

CREATE TABLE IF NOT EXISTS wickets (
    id BIGSERIAL PRIMARY KEY,
    delivery_id BIGINT REFERENCES deliveries(id) ON DELETE CASCADE,
    player_out_id BIGINT REFERENCES players(id),
    kind VARCHAR(50)
);

CREATE TABLE IF NOT EXISTS wicket_fielders (
    wicket_id BIGINT REFERENCES wickets(id) ON DELETE CASCADE,
    fielder_id BIGINT REFERENCES players(id),
    PRIMARY KEY (wicket_id, fielder_id)
);

-- NEW: Powerplays table
CREATE TABLE IF NOT EXISTS powerplays (
    id BIGSERIAL PRIMARY KEY,
    innings_id BIGINT REFERENCES innings(id) ON DELETE CASCADE,
    start_over NUMERIC(4,1),
    end_over NUMERIC(4,1),
    type VARCHAR(20)
);

-- INDEXES
CREATE INDEX IF NOT EXISTS idx_matches_event ON matches(event_id);
CREATE INDEX IF NOT EXISTS idx_innings_match ON innings(match_id);
CREATE INDEX IF NOT EXISTS idx_overs_innings ON overs(innings_id);
CREATE INDEX IF NOT EXISTS idx_deliveries_over ON deliveries(over_id);
CREATE INDEX IF NOT EXISTS idx_deliveries_batter ON deliveries(batter_id);
CREATE INDEX IF NOT EXISTS idx_deliveries_bowler ON deliveries(bowler_id);
CREATE INDEX IF NOT EXISTS idx_wickets_delivery ON wickets(delivery_id);
CREATE INDEX IF NOT EXISTS idx_powerplays_innings ON powerplays(innings_id);

";

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                Log.Information("Cricket schema created successfully.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Log.Information($"Schema creation failed: {ex.Message}");
            }
        }
    }
}
