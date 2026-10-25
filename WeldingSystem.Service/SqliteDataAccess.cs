using System;
using System.Data.SQLite;
using System.IO;
using WeldingSystem.Contracts;

namespace WeldingSystem.Service
{
    /// <summary>
    /// Raw ADO.NET data access layer \u2014 deliberately no EF Core, matching the original
    /// PRI 7000-style direct SQLiteConnection/SQLiteCommand/SQLiteDataReader usage.
    /// Thread safety: SQLite file-mode connections are opened per-call (connection pooling
    /// handled by the provider) so this class is safe to call concurrently from multiple
    /// duplex sessions without external locking.
    /// </summary>
    public class SqliteDataAccess
    {
        private readonly string _connectionString;

        public SqliteDataAccess(string dbPath)
        {
            bool isNewDb = !File.Exists(dbPath);
            _connectionString = $"Data Source={dbPath};Version=3;";

            if (isNewDb)
            {
                SQLiteConnection.CreateFile(dbPath);
            }
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();

                ExecuteNonQuery(conn, @"
                    CREATE TABLE IF NOT EXISTS WeldingJobs (
                        JobId TEXT PRIMARY KEY,
                        MachineId TEXT NOT NULL,
                        StartedUtc TEXT NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'InProgress'
                    );");

                ExecuteNonQuery(conn, @"
                    CREATE TABLE IF NOT EXISTS TelemetryLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MachineId TEXT NOT NULL,
                        JobId TEXT,
                        CurrentAmps REAL NOT NULL,
                        VoltageVolts REAL NOT NULL,
                        WeldDurationMs REAL NOT NULL,
                        ErrorCode INTEGER NOT NULL DEFAULT 0,
                        TimestampUnixMs INTEGER NOT NULL
                    );");

                ExecuteNonQuery(conn, @"
                    CREATE TABLE IF NOT EXISTS DeviceStatus (
                        MachineId TEXT PRIMARY KEY,
                        State TEXT NOT NULL,
                        LastUpdatedUnixMs INTEGER NOT NULL
                    );");
            }
        }

        private static void ExecuteNonQuery(SQLiteConnection conn, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Inserts a telemetry row and upserts the WeldingJobs/DeviceStatus rows,
        /// all inside one transaction so partial writes can't leave inconsistent state
        /// (same atomicity concern as the Unit of Work pattern discussed in Day 5's revision).</summary>
        public void SaveTelemetry(WeldingEventDto telemetry)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = new SQLiteCommand(@"
                            INSERT INTO TelemetryLogs
                                (MachineId, JobId, CurrentAmps, VoltageVolts, WeldDurationMs, ErrorCode, TimestampUnixMs)
                            VALUES
                                (@MachineId, @JobId, @CurrentAmps, @VoltageVolts, @WeldDurationMs, @ErrorCode, @TimestampUnixMs);",
                            conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@MachineId", telemetry.MachineId);
                            cmd.Parameters.AddWithValue("@JobId", telemetry.JobId ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@CurrentAmps", telemetry.CurrentAmps);
                            cmd.Parameters.AddWithValue("@VoltageVolts", telemetry.VoltageVolts);
                            cmd.Parameters.AddWithValue("@WeldDurationMs", telemetry.WeldDurationMs);
                            cmd.Parameters.AddWithValue("@ErrorCode", telemetry.ErrorCode);
                            cmd.Parameters.AddWithValue("@TimestampUnixMs", telemetry.TimestampUnixMs);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SQLiteCommand(@"
                            INSERT INTO WeldingJobs (JobId, MachineId, StartedUtc, Status)
                            VALUES (@JobId, @MachineId, @StartedUtc, 'InProgress')
                            ON CONFLICT(JobId) DO NOTHING;",
                            conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@JobId", telemetry.JobId);
                            cmd.Parameters.AddWithValue("@MachineId", telemetry.MachineId);
                            cmd.Parameters.AddWithValue("@StartedUtc", DateTime.UtcNow.ToString("o"));
                            cmd.ExecuteNonQuery();
                        }

                        string state = telemetry.ErrorCode != 0 ? "Fault" : "Running";
                        using (var cmd = new SQLiteCommand(@"
                            INSERT INTO DeviceStatus (MachineId, State, LastUpdatedUnixMs)
                            VALUES (@MachineId, @State, @Ts)
                            ON CONFLICT(MachineId) DO UPDATE SET State = @State, LastUpdatedUnixMs = @Ts;",
                            conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@MachineId", telemetry.MachineId);
                            cmd.Parameters.AddWithValue("@State", state);
                            cmd.Parameters.AddWithValue("@Ts", telemetry.TimestampUnixMs);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public DeviceStatusDto GetDeviceStatus(string machineId)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(
                    "SELECT MachineId, State, LastUpdatedUnixMs FROM DeviceStatus WHERE MachineId = @MachineId;", conn))
                {
                    cmd.Parameters.AddWithValue("@MachineId", machineId);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new DeviceStatusDto
                            {
                                MachineId = reader.GetString(0),
                                State = reader.GetString(1),
                                LastUpdatedUnixMs = reader.GetInt64(2)
                            };
                        }
                    }
                }
            }

            return new DeviceStatusDto { MachineId = machineId, State = "Unknown", LastUpdatedUnixMs = 0 };
        }
    }
}
