using System.Text.Json;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

/// <summary>
/// Durable on-disk queue so RDP events survive a temporary loss of connectivity to the central server.
/// Events are appended as JSON lines; a background flush loop tries to deliver them in order and
/// only trims the file once the server has accepted the batch.
/// </summary>
public class LocalEventQueue
{
    private readonly string _path;
    private readonly object _lock = new();

    public LocalEventQueue()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RdpMonitor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "events.queue.jsonl");
    }

    public void Enqueue(RdpEventDto evt)
    {
        lock (_lock)
        {
            var line = JsonSerializer.Serialize(evt);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    /// <summary>Reads up to <paramref name="maxCount"/> queued events without removing them.</summary>
    public List<RdpEventDto> Peek(int maxCount)
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return new List<RdpEventDto>();

            var result = new List<RdpEventDto>();
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize<RdpEventDto>(line);
                    if (evt is not null) result.Add(evt);
                }
                catch
                {
                    // skip malformed line
                }
                if (result.Count >= maxCount) break;
            }
            return result;
        }
    }

    /// <summary>Removes the first <paramref name="count"/> events from the queue (call after a successful send).</summary>
    public void RemoveFirst(int count)
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return;

            var allLines = File.ReadAllLines(_path);
            var remaining = allLines.Skip(count).ToArray();
            File.WriteAllLines(_path, remaining);
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return 0;
            return File.ReadLines(_path).Count(l => !string.IsNullOrWhiteSpace(l));
        }
    }
}
