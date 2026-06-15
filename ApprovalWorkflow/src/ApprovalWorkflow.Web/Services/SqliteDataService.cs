using Microsoft.Data.Sqlite;
using Microsoft.Xrm.Sdk;
using ApprovalWorkflow.Web.Models;

namespace ApprovalWorkflow.Web.Services;

/// <summary>
/// SQLite-backed implementation of IDataService.
/// Drop-in replacement for DataverseService for local / demo use.
/// Creates the schema automatically on first run; data persists across restarts.
/// </summary>
public class SqliteDataService : IDataService
{
    private readonly string _connectionString;
    private readonly bool   _demoMode;

    public SqliteDataService(string dbPath, bool demoMode = false)
    {
        _connectionString = $"Data Source={dbPath}";
        _demoMode         = demoMode;
        EnsureSchema();
    }

    // ── Connection helper ───────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ── Schema creation ─────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS WorkflowDefinition (
                Id           TEXT PRIMARY KEY,
                Name         TEXT NOT NULL,
                DocumentType TEXT NOT NULL,
                Description  TEXT,
                Active       INTEGER NOT NULL DEFAULT 1,
                CreatedOn    TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS WorkflowTier (
                Id                   TEXT PRIMARY KEY,
                WorkflowDefinitionId TEXT NOT NULL,
                TierNumber           INTEGER NOT NULL,
                TierName             TEXT NOT NULL,
                ApproverEmails       TEXT NOT NULL,
                ApproverType         INTEGER NOT NULL DEFAULT 1,
                RequiredApprovals    INTEGER NOT NULL DEFAULT 1,
                ReminderHours        INTEGER NOT NULL DEFAULT 24,
                ExpiryHours          INTEGER NOT NULL DEFAULT 72
            );
            CREATE TABLE IF NOT EXISTS ApprovalRequest (
                Id                   TEXT PRIMARY KEY,
                Title                TEXT NOT NULL,
                RequestedBy          TEXT NOT NULL,
                WorkflowDefinitionId TEXT NOT NULL,
                Status               INTEGER NOT NULL DEFAULT 1,
                CurrentTierNumber    INTEGER NOT NULL DEFAULT 1,
                LogicAppRunId        TEXT,
                LogicAppRunUrl       TEXT,
                DocumentTitle        TEXT NOT NULL,
                DocumentSummary      TEXT,
                DocumentContent      TEXT,
                ExpiryDate           TEXT NOT NULL,
                ResetCount           INTEGER NOT NULL DEFAULT 0,
                CreatedOn            TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS ApprovalTierProgress (
                Id                TEXT PRIMARY KEY,
                ApprovalRequestId TEXT NOT NULL,
                WorkflowTierId    TEXT NOT NULL,
                TierNumber        INTEGER NOT NULL,
                TierName          TEXT NOT NULL,
                Status            INTEGER NOT NULL DEFAULT 1,
                AssignedApprover  TEXT NOT NULL,
                ApprovalTokenHash TEXT NOT NULL DEFAULT '',
                TokenConsumed     INTEGER NOT NULL DEFAULT 0,
                CallbackUrl       TEXT,
                ReviewUrl         TEXT,
                Decision          INTEGER NOT NULL DEFAULT 1,
                ApproverComments  TEXT,
                DecisionDate      TEXT,
                LastRemindedAt    TEXT,
                DelegatedFrom     TEXT,
                CreatedOn         TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS AuditLog (
                Id                TEXT PRIMARY KEY,
                ApprovalRequestId TEXT NOT NULL,
                TierNumber        INTEGER NOT NULL DEFAULT 0,
                EventType         INTEGER NOT NULL DEFAULT 1,
                PerformedBy       TEXT NOT NULL,
                PreviousValue     TEXT,
                NewValue          TEXT,
                IpAddress         TEXT,
                Timestamp         TEXT NOT NULL,
                Notes             TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── WorkflowDefinitions ─────────────────────────────────────────────────

    public async Task<List<WorkflowDefinition>> GetActiveWorkflowDefinitionsAsync()
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM WorkflowDefinition WHERE Active=1 ORDER BY Name";
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<WorkflowDefinition>();
        while (await reader.ReadAsync()) list.Add(ReadWorkflowDefinition(reader));
        return list;
    }

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(Guid id)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM WorkflowDefinition WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadWorkflowDefinition(reader) : null;
    }

    public async Task<Guid> CreateWorkflowDefinitionAsync(WorkflowDefinition def)
    {
        def.Id = Guid.NewGuid();
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WorkflowDefinition (Id, Name, DocumentType, Description, Active)
            VALUES (@id, @name, @dt, @desc, @active)
            """;
        cmd.Parameters.AddWithValue("@id",     def.Id.ToString());
        cmd.Parameters.AddWithValue("@name",   def.Name);
        cmd.Parameters.AddWithValue("@dt",     def.DocumentType);
        cmd.Parameters.AddWithValue("@desc",   (object?)def.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", def.Active ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
        return def.Id;
    }

    public async Task UpdateWorkflowDefinitionAsync(Guid id, Dictionary<string, object> updates)
    {
        if (!updates.Any()) return;
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        var sets = BuildSetClause(updates, cmd);
        cmd.CommandText = $"UPDATE WorkflowDefinition SET {sets} WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── WorkflowTiers ───────────────────────────────────────────────────────

    public async Task<List<WorkflowTier>> GetTiersForDefinitionAsync(Guid definitionId)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM WorkflowTier WHERE WorkflowDefinitionId=@did ORDER BY TierNumber";
        cmd.Parameters.AddWithValue("@did", definitionId.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<WorkflowTier>();
        while (await reader.ReadAsync()) list.Add(ReadWorkflowTier(reader));
        return list;
    }

    public async Task<Guid> CreateWorkflowTierAsync(WorkflowTier tier)
    {
        tier.Id = Guid.NewGuid();
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO WorkflowTier (Id, WorkflowDefinitionId, TierNumber, TierName,
                ApproverEmails, ApproverType, RequiredApprovals, ReminderHours, ExpiryHours)
            VALUES (@id, @did, @tn, @tname, @emails, @atype, @req, @rem, @exp)
            """;
        cmd.Parameters.AddWithValue("@id",     tier.Id.ToString());
        cmd.Parameters.AddWithValue("@did",    tier.WorkflowDefinitionId.ToString());
        cmd.Parameters.AddWithValue("@tn",     tier.TierNumber);
        cmd.Parameters.AddWithValue("@tname",  tier.TierName);
        cmd.Parameters.AddWithValue("@emails", tier.ApproverEmails);
        cmd.Parameters.AddWithValue("@atype",  tier.ApproverType);
        cmd.Parameters.AddWithValue("@req",    tier.RequiredApprovals);
        cmd.Parameters.AddWithValue("@rem",    tier.ReminderHours);
        cmd.Parameters.AddWithValue("@exp",    tier.ExpiryHours);
        await cmd.ExecuteNonQueryAsync();
        return tier.Id;
    }

    public async Task UpdateWorkflowTierAsync(Guid tierId, Dictionary<string, object> updates)
    {
        if (!updates.Any()) return;
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        var sets = BuildSetClause(updates, cmd);
        cmd.CommandText = $"UPDATE WorkflowTier SET {sets} WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", tierId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteWorkflowTierAsync(Guid tierId)
    {
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM WorkflowTier WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", tierId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── ApprovalRequests ────────────────────────────────────────────────────

    public async Task<Guid> CreateApprovalRequestAsync(ApprovalRequest req)
    {
        req.Id = Guid.NewGuid();
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ApprovalRequest (Id, Title, RequestedBy, WorkflowDefinitionId, Status,
                CurrentTierNumber, LogicAppRunId, LogicAppRunUrl, DocumentTitle, DocumentSummary,
                DocumentContent, ExpiryDate, ResetCount)
            VALUES (@id,@title,@by,@wid,@status,@tier,@runid,@runurl,@dtitle,@dsum,@dcontent,@exp,@reset)
            """;
        cmd.Parameters.AddWithValue("@id",       req.Id.ToString());
        cmd.Parameters.AddWithValue("@title",    req.Title);
        cmd.Parameters.AddWithValue("@by",       req.RequestedBy);
        cmd.Parameters.AddWithValue("@wid",      req.WorkflowDefinitionId.ToString());
        cmd.Parameters.AddWithValue("@status",   (int)req.Status);
        cmd.Parameters.AddWithValue("@tier",     req.CurrentTierNumber);
        cmd.Parameters.AddWithValue("@runid",    (object?)req.LogicAppRunId    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@runurl",   (object?)req.LogicAppRunUrl   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dtitle",   req.DocumentTitle);
        cmd.Parameters.AddWithValue("@dsum",     (object?)req.DocumentSummary  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dcontent", (object?)req.DocumentContent  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exp",      req.ExpiryDate.ToString("O"));
        cmd.Parameters.AddWithValue("@reset",    req.ResetCount);
        await cmd.ExecuteNonQueryAsync();
        return req.Id;
    }

    public async Task<ApprovalRequest?> GetApprovalRequestAsync(Guid id)
    {
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.*, d.Name AS DefinitionName
            FROM ApprovalRequest r
            LEFT JOIN WorkflowDefinition d ON d.Id = r.WorkflowDefinitionId
            WHERE r.Id=@id
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadApprovalRequestWithName(reader) : null;
    }

    public async Task<List<ApprovalRequest>> GetAllApprovalRequestsAsync()
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = """
            SELECT r.*, d.Name AS DefinitionName
            FROM ApprovalRequest r
            LEFT JOIN WorkflowDefinition d ON d.Id = r.WorkflowDefinitionId
            ORDER BY r.CreatedOn DESC
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ApprovalRequest>();
        while (await reader.ReadAsync()) list.Add(ReadApprovalRequestWithName(reader));
        return list;
    }

    public async Task<List<ApprovalRequest>> GetExpiredPendingRequestsAsync()
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM ApprovalRequest WHERE Status=1 AND ExpiryDate < @now";
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ApprovalRequest>();
        while (await reader.ReadAsync()) list.Add(ReadApprovalRequest(reader));
        return list;
    }

    public async Task UpdateApprovalRequestAsync(Guid id, Dictionary<string, object> updates)
    {
        if (!updates.Any()) return;
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        var sets = BuildSetClause(updates, cmd);
        cmd.CommandText = $"UPDATE ApprovalRequest SET {sets} WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── ApprovalTierProgress ────────────────────────────────────────────────

    public async Task<Guid> CreateTierProgressAsync(ApprovalTierProgress tp)
    {
        tp.Id = Guid.NewGuid();
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ApprovalTierProgress (Id, ApprovalRequestId, WorkflowTierId, TierNumber,
                TierName, Status, AssignedApprover, ApprovalTokenHash, TokenConsumed, CallbackUrl,
                ReviewUrl, Decision, ApproverComments, DecisionDate, LastRemindedAt, DelegatedFrom)
            VALUES (@id,@rid,@tid,@tn,@tname,@status,@approver,@tokenhash,@consumed,
                    @cburl,@rvurl,@decision,@comments,@decdate,@reminded,@delfrom)
            """;
        cmd.Parameters.AddWithValue("@id",        tp.Id.ToString());
        cmd.Parameters.AddWithValue("@rid",       tp.ApprovalRequestId.ToString());
        cmd.Parameters.AddWithValue("@tid",       tp.WorkflowTierId.ToString());
        cmd.Parameters.AddWithValue("@tn",        tp.TierNumber);
        cmd.Parameters.AddWithValue("@tname",     tp.TierName);
        cmd.Parameters.AddWithValue("@status",    (int)tp.Status);
        cmd.Parameters.AddWithValue("@approver",  tp.AssignedApprover);
        cmd.Parameters.AddWithValue("@tokenhash", tp.ApprovalTokenHash);
        cmd.Parameters.AddWithValue("@consumed",  tp.TokenConsumed ? 1 : 0);
        cmd.Parameters.AddWithValue("@cburl",     (object?)tp.CallbackUrl       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rvurl",     (object?)tp.ReviewUrl         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@decision",  (int)tp.Decision);
        cmd.Parameters.AddWithValue("@comments",  (object?)tp.ApproverComments  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@decdate",   (object?)tp.DecisionDate?.ToString("O")    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reminded",  (object?)tp.LastRemindedAt?.ToString("O")  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@delfrom",   (object?)tp.DelegatedFrom     ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return tp.Id;
    }

    public async Task<ApprovalTierProgress?> GetTierProgressByTokenHashAsync(string tokenHash)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM ApprovalTierProgress WHERE ApprovalTokenHash=@h";
        cmd.Parameters.AddWithValue("@h", tokenHash);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadTierProgress(reader) : null;
    }

    public async Task<List<ApprovalTierProgress>> GetTierProgressForRequestAsync(Guid requestId)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM ApprovalTierProgress WHERE ApprovalRequestId=@rid ORDER BY TierNumber";
        cmd.Parameters.AddWithValue("@rid", requestId.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ApprovalTierProgress>();
        while (await reader.ReadAsync()) list.Add(ReadTierProgress(reader));
        return list;
    }

    public async Task<ApprovalTierProgress?> GetCurrentPendingTierAsync(Guid requestId)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        // Status=2 = TierStatus.Pending
        cmd.CommandText  = """
            SELECT * FROM ApprovalTierProgress
            WHERE ApprovalRequestId=@rid AND Status=2
            ORDER BY TierNumber LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@rid", requestId.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadTierProgress(reader) : null;
    }

    public async Task<List<ApprovalTierProgress>> GetOverdueRemindersAsync()
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        // In DemoMode treat ReminderHours as minutes (use 2-minute cutoff);
        // in production use the standard 24-hour cutoff.
        var cutoff = (_demoMode
            ? DateTime.UtcNow.AddMinutes(-2)
            : DateTime.UtcNow.AddHours(-24)).ToString("O");
        cmd.CommandText  = """
            SELECT * FROM ApprovalTierProgress
            WHERE Status=2
              AND TokenConsumed=0
              AND ReviewUrl IS NOT NULL
              AND ReviewUrl != ''
              AND (LastRemindedAt IS NULL OR LastRemindedAt < @cutoff)
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ApprovalTierProgress>();
        while (await reader.ReadAsync()) list.Add(ReadTierProgress(reader));
        return list;
    }

    public async Task UpdateTierProgressAsync(Guid tierId, Dictionary<string, object> updates)
    {
        if (!updates.Any()) return;
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        var sets = BuildSetClause(updates, cmd);
        cmd.CommandText = $"UPDATE ApprovalTierProgress SET {sets} WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", tierId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ApprovalTierProgress?> GetTierProgressByIdAsync(Guid tierId)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM ApprovalTierProgress WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", tierId.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadTierProgress(reader) : null;
    }

    public async Task<List<ApprovalTierProgress>> GetTierProgressRowsForTierAsync(Guid requestId, int tierNumber)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM ApprovalTierProgress WHERE ApprovalRequestId=@rid AND TierNumber=@tn ORDER BY CreatedOn";
        cmd.Parameters.AddWithValue("@rid", requestId.ToString());
        cmd.Parameters.AddWithValue("@tn",  tierNumber);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ApprovalTierProgress>();
        while (await reader.ReadAsync()) list.Add(ReadTierProgress(reader));
        return list;
    }

    public async Task ResetTierProgressForRequestAsync(Guid requestId)
    {
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ApprovalTierProgress WHERE ApprovalRequestId=@rid";
        cmd.Parameters.AddWithValue("@rid", requestId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── AuditLog ────────────────────────────────────────────────────────────

    public async Task WriteAuditLogAsync(AuditLogEntry entry)
    {
        entry.Id = Guid.NewGuid();
        using var conn  = Open();
        using var cmd   = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuditLog (Id, ApprovalRequestId, TierNumber, EventType, PerformedBy,
                PreviousValue, NewValue, IpAddress, Timestamp, Notes)
            VALUES (@id,@rid,@tn,@et,@by,@prev,@new,@ip,@ts,@notes)
            """;
        cmd.Parameters.AddWithValue("@id",    entry.Id.ToString());
        cmd.Parameters.AddWithValue("@rid",   entry.ApprovalRequestId.ToString());
        cmd.Parameters.AddWithValue("@tn",    entry.TierNumber);
        cmd.Parameters.AddWithValue("@et",    (int)entry.EventType);
        cmd.Parameters.AddWithValue("@by",    entry.PerformedBy);
        cmd.Parameters.AddWithValue("@prev",  (object?)entry.PreviousValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@new",   (object?)entry.NewValue      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ip",    (object?)entry.IpAddress     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ts",    DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@notes", (object?)entry.Notes         ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AuditLogEntry>> GetAuditLogForRequestAsync(Guid requestId)
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = "SELECT * FROM AuditLog WHERE ApprovalRequestId=@rid ORDER BY Timestamp";
        cmd.Parameters.AddWithValue("@rid", requestId.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<AuditLogEntry>();
        while (await reader.ReadAsync()) list.Add(ReadAuditLogEntry(reader));
        return list;
    }

    public async Task<List<AuditLogEntry>> GetAllAuditLogsAsync()
    {
        using var conn   = Open();
        using var cmd    = conn.CreateCommand();
        cmd.CommandText  = """
            SELECT l.*, r.Title AS RequestTitle
            FROM AuditLog l
            LEFT JOIN ApprovalRequest r ON r.Id = l.ApprovalRequestId
            ORDER BY l.Timestamp DESC
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<AuditLogEntry>();
        while (await reader.ReadAsync())
        {
            var entry = ReadAuditLogEntry(reader);
            try
            {
                var ord = reader.GetOrdinal("RequestTitle");
                if (!reader.IsDBNull(ord)) entry.RequestTitle = reader.GetString(ord);
            }
            catch { /* column not present — safe to ignore */ }
            list.Add(entry);
        }
        return list;
    }

    // ── Reader helpers ───────────────────────────────────────────────────────

    private static WorkflowDefinition ReadWorkflowDefinition(SqliteDataReader r) => new()
    {
        Id           = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        Name         = r.GetString(r.GetOrdinal("Name")),
        DocumentType = r.GetString(r.GetOrdinal("DocumentType")),
        Description  = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        Active       = r.GetInt64(r.GetOrdinal("Active")) == 1,
        CreatedOn    = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedOn")))
    };

    private static WorkflowTier ReadWorkflowTier(SqliteDataReader r) => new()
    {
        Id                   = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        WorkflowDefinitionId = Guid.Parse(r.GetString(r.GetOrdinal("WorkflowDefinitionId"))),
        TierNumber           = (int)r.GetInt64(r.GetOrdinal("TierNumber")),
        TierName             = r.GetString(r.GetOrdinal("TierName")),
        ApproverEmails       = r.GetString(r.GetOrdinal("ApproverEmails")),
        ApproverType         = (int)r.GetInt64(r.GetOrdinal("ApproverType")),
        RequiredApprovals    = (int)r.GetInt64(r.GetOrdinal("RequiredApprovals")),
        ReminderHours        = (int)r.GetInt64(r.GetOrdinal("ReminderHours")),
        ExpiryHours          = (int)r.GetInt64(r.GetOrdinal("ExpiryHours"))
    };

    private static ApprovalRequest ReadApprovalRequest(SqliteDataReader r) => new()
    {
        Id                   = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        Title                = r.GetString(r.GetOrdinal("Title")),
        RequestedBy          = r.GetString(r.GetOrdinal("RequestedBy")),
        WorkflowDefinitionId = Guid.Parse(r.GetString(r.GetOrdinal("WorkflowDefinitionId"))),
        Status               = (ApprovalStatus)(int)r.GetInt64(r.GetOrdinal("Status")),
        CurrentTierNumber    = (int)r.GetInt64(r.GetOrdinal("CurrentTierNumber")),
        LogicAppRunId        = r.IsDBNull(r.GetOrdinal("LogicAppRunId"))  ? null : r.GetString(r.GetOrdinal("LogicAppRunId")),
        LogicAppRunUrl       = r.IsDBNull(r.GetOrdinal("LogicAppRunUrl")) ? null : r.GetString(r.GetOrdinal("LogicAppRunUrl")),
        DocumentTitle        = r.GetString(r.GetOrdinal("DocumentTitle")),
        DocumentSummary      = r.IsDBNull(r.GetOrdinal("DocumentSummary"))  ? null : r.GetString(r.GetOrdinal("DocumentSummary")),
        DocumentContent      = r.IsDBNull(r.GetOrdinal("DocumentContent"))  ? null : r.GetString(r.GetOrdinal("DocumentContent")),
        ExpiryDate           = DateTime.Parse(r.GetString(r.GetOrdinal("ExpiryDate"))),
        ResetCount           = (int)r.GetInt64(r.GetOrdinal("ResetCount")),
        CreatedOn            = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedOn")))
    };

    private static ApprovalRequest ReadApprovalRequestWithName(SqliteDataReader r)
    {
        var req = ReadApprovalRequest(r);
        try
        {
            var ord = r.GetOrdinal("DefinitionName");
            if (!r.IsDBNull(ord)) req.WorkflowDefinitionName = r.GetString(ord);
        }
        catch { /* column absent — safe to ignore */ }
        return req;
    }

    private static ApprovalTierProgress ReadTierProgress(SqliteDataReader r) => new()
    {
        Id                = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        ApprovalRequestId = Guid.Parse(r.GetString(r.GetOrdinal("ApprovalRequestId"))),
        WorkflowTierId    = Guid.Parse(r.GetString(r.GetOrdinal("WorkflowTierId"))),
        TierNumber        = (int)r.GetInt64(r.GetOrdinal("TierNumber")),
        TierName          = r.GetString(r.GetOrdinal("TierName")),
        Status            = (TierStatus)(int)r.GetInt64(r.GetOrdinal("Status")),
        AssignedApprover  = r.GetString(r.GetOrdinal("AssignedApprover")),
        ApprovalTokenHash = r.GetString(r.GetOrdinal("ApprovalTokenHash")),
        TokenConsumed     = r.GetInt64(r.GetOrdinal("TokenConsumed")) == 1,
        CallbackUrl       = r.IsDBNull(r.GetOrdinal("CallbackUrl"))       ? null : r.GetString(r.GetOrdinal("CallbackUrl")),
        ReviewUrl         = r.IsDBNull(r.GetOrdinal("ReviewUrl"))         ? null : r.GetString(r.GetOrdinal("ReviewUrl")),
        Decision          = (TierDecision)(int)r.GetInt64(r.GetOrdinal("Decision")),
        ApproverComments  = r.IsDBNull(r.GetOrdinal("ApproverComments"))  ? null : r.GetString(r.GetOrdinal("ApproverComments")),
        DecisionDate      = r.IsDBNull(r.GetOrdinal("DecisionDate"))      ? null : DateTime.Parse(r.GetString(r.GetOrdinal("DecisionDate"))),
        LastRemindedAt    = r.IsDBNull(r.GetOrdinal("LastRemindedAt"))    ? null : DateTime.Parse(r.GetString(r.GetOrdinal("LastRemindedAt"))),
        DelegatedFrom     = r.IsDBNull(r.GetOrdinal("DelegatedFrom"))     ? null : r.GetString(r.GetOrdinal("DelegatedFrom")),
        CreatedOn         = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedOn")))
    };

    private static AuditLogEntry ReadAuditLogEntry(SqliteDataReader r) => new()
    {
        Id                = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        ApprovalRequestId = Guid.Parse(r.GetString(r.GetOrdinal("ApprovalRequestId"))),
        TierNumber        = (int)r.GetInt64(r.GetOrdinal("TierNumber")),
        EventType         = (AuditEventType)(int)r.GetInt64(r.GetOrdinal("EventType")),
        PerformedBy       = r.GetString(r.GetOrdinal("PerformedBy")),
        PreviousValue     = r.IsDBNull(r.GetOrdinal("PreviousValue")) ? null : r.GetString(r.GetOrdinal("PreviousValue")),
        NewValue          = r.IsDBNull(r.GetOrdinal("NewValue"))      ? null : r.GetString(r.GetOrdinal("NewValue")),
        IpAddress         = r.IsDBNull(r.GetOrdinal("IpAddress"))     ? null : r.GetString(r.GetOrdinal("IpAddress")),
        Timestamp         = DateTime.Parse(r.GetString(r.GetOrdinal("Timestamp"))),
        Notes             = r.IsDBNull(r.GetOrdinal("Notes"))         ? null : r.GetString(r.GetOrdinal("Notes"))
    };

    // ── Update dictionary helpers ────────────────────────────────────────────
    // Callers pass Dataverse-style cr_* keys and may wrap ints in OptionSetValue.
    // We map keys to SQLite column names and unwrap values here.

    private static string BuildSetClause(Dictionary<string, object> updates, SqliteCommand cmd)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var kv in updates)
        {
            var col = MapColumn(kv.Key);
            var val = UnwrapValue(kv.Value);
            var p   = $"@u{i++}";
            cmd.Parameters.AddWithValue(p, val ?? DBNull.Value);
            parts.Add($"{col}={p}");
        }
        return string.Join(", ", parts);
    }

    private static object? UnwrapValue(object? val) => val switch
    {
        OptionSetValue osv  => osv.Value,
        bool b              => b ? 1 : 0,
        DateTime dt         => dt.ToString("O"),
        _                   => val
    };

    private static string MapColumn(string key) => key switch
    {
        "cr_name"               => "Name",
        "cr_documenttype"       => "DocumentType",
        "cr_description"        => "Description",
        "cr_active"             => "Active",
        "cr_tiernumber"         => "TierNumber",
        "cr_tiername"           => "TierName",
        "cr_approveremails"     => "ApproverEmails",
        "cr_approvertype"       => "ApproverType",
        "cr_requiredapprovals"  => "RequiredApprovals",
        "cr_reminderhours"      => "ReminderHours",
        "cr_expiryhours"        => "ExpiryHours",
        "cr_title"              => "Title",
        "cr_requestedby"        => "RequestedBy",
        "cr_status"             => "Status",
        "cr_currenttiernumber"  => "CurrentTierNumber",
        "cr_logicapprunid"      => "LogicAppRunId",
        "cr_logicapprunurl"     => "LogicAppRunUrl",
        "cr_documenttitle"      => "DocumentTitle",
        "cr_documentsummary"    => "DocumentSummary",
        "cr_documentcontent"    => "DocumentContent",
        "cr_expirydate"         => "ExpiryDate",
        "cr_resetcount"         => "ResetCount",
        "cr_assignedapprover"   => "AssignedApprover",
        "cr_approvaltokenhash"  => "ApprovalTokenHash",
        "cr_tokenconsumed"      => "TokenConsumed",
        "cr_callbackurl"        => "CallbackUrl",
        "cr_reviewurl"          => "ReviewUrl",
        "cr_decision"           => "Decision",
        "cr_approvercomments"   => "ApproverComments",
        "cr_decisiondate"       => "DecisionDate",
        "cr_lastreminded_at"    => "LastRemindedAt",
        "cr_delegatedfrom"      => "DelegatedFrom",
        _                       => key   // pass through if already a column name
    };
}
