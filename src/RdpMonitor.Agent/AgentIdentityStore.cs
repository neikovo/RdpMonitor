using System.Text.Json;

namespace RdpMonitor.Agent;

public class AgentIdentity
{
    public Guid AgentId { get; set; }
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// Persists the agent's identity (its GUID and the API key issued by the server on first registration)
/// so re-registration isn't needed after a service restart.
/// </summary>
public class AgentIdentityStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public AgentIdentityStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RdpMonitor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "agent-state.json");
    }

    public AgentIdentity LoadOrCreate()
    {
        lock (_lock)
        {
            if (File.Exists(_path))
            {
                try
                {
                    var json = File.ReadAllText(_path);
                    var existing = JsonSerializer.Deserialize<AgentIdentity>(json);
                    if (existing is not null && existing.AgentId != Guid.Empty)
                        return existing;
                }
                catch
                {
                    // corrupt file — fall through and regenerate
                }
            }

            var identity = new AgentIdentity { AgentId = Guid.NewGuid(), ApiKey = "" };
            Save(identity);
            return identity;
        }
    }

    public void Save(AgentIdentity identity)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}
