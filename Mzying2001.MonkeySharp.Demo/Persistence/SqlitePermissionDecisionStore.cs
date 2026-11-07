using Mzying2001.MonkeySharp.Core.Permissions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Mzying2001.MonkeySharp.Demo.Persistence
{
    public sealed class SqlitePermissionDecisionStore : IUserScriptPermissionDecisionStore
    {
        private readonly SqliteDatabase _database;
        public SqlitePermissionDecisionStore(SqliteDatabase database) { _database = database; }
        public Task<IReadOnlyList<UserScriptPermissionScope>> ListAsync(CancellationToken token) => Task.Run<IReadOnlyList<UserScriptPermissionScope>>(() =>
        {
            token.ThrowIfCancellationRequested();
            var result = new List<UserScriptPermissionScope>();
            using (var connection = _database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT script_key,kind,target FROM permission_decisions ORDER BY script_key,kind,target";
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(new UserScriptPermissionScope(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
            return result.AsReadOnly();
        }, token);
        public Task<PermissionDecision?> GetAsync(UserScriptPermissionScope scope, CancellationToken token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using (var connection = _database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT decision FROM permission_decisions WHERE script_key=$script AND kind=$kind AND target=$target";
                command.Parameters.AddWithValue("$script", scope.ScriptKey); command.Parameters.AddWithValue("$kind", scope.Kind); command.Parameters.AddWithValue("$target", scope.Target);
                var value = command.ExecuteScalar();
                return value == null ? (PermissionDecision?)null : Convert.ToInt32(value) == 0 ? PermissionDecision.Allow : PermissionDecision.Deny;
            }
        }, token);
        public Task SetAsync(UserScriptPermissionScope scope, PermissionDecision decision, CancellationToken token) => Task.Run(() =>
        {
            if (decision != PermissionDecision.Allow && decision != PermissionDecision.Deny) throw new ArgumentException("Only terminal choices can be stored.");
            token.ThrowIfCancellationRequested();
            using (var connection = _database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO permission_decisions(script_key,kind,target,decision) VALUES($script,$kind,$target,$decision) ON CONFLICT(script_key,kind,target) DO UPDATE SET decision=excluded.decision";
                command.Parameters.AddWithValue("$script", scope.ScriptKey); command.Parameters.AddWithValue("$kind", scope.Kind); command.Parameters.AddWithValue("$target", scope.Target);
                command.Parameters.AddWithValue("$decision", decision == PermissionDecision.Allow ? 0 : 1); command.ExecuteNonQuery();
            }
        }, token);
        public Task RevokeAsync(string scriptKey, CancellationToken token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using (var connection = _database.OpenReady())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = scriptKey == null ? "DELETE FROM permission_decisions" : "DELETE FROM permission_decisions WHERE script_key=$script";
                if (scriptKey != null) command.Parameters.AddWithValue("$script", scriptKey);
                command.ExecuteNonQuery();
            }
        }, token);
    }
}
