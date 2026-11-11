using System.Collections.Concurrent;

namespace MarbookApi.Services;

public class PresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<int, HashSet<string>> _onlineUsers = new();

    public Task<bool> UserConnected(int userId, string connectionId)
    {
        var isFirstConnection = false;

        _onlineUsers.AddOrUpdate(
            userId,
            _ =>
            {
                isFirstConnection = true;
                return new HashSet<string> { connectionId };
            },
            (_, connections) =>
            {
                lock (connections)
                {
                    if (connections.Count == 0)
                    {
                        isFirstConnection = true;
                    }
                    connections.Add(connectionId);
                }
                return connections;
            }
        );

        return Task.FromResult(isFirstConnection);
    }

    public Task<bool> UserDisconnected(int userId, string connectionId)
    {
        var isLastConnection = false;

        if (_onlineUsers.TryGetValue(userId, out var connections))
        {
            lock (connections)
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    isLastConnection = true;
                }
            }

            if (isLastConnection)
            {
                _onlineUsers.TryRemove(userId, out _);
            }
        }

        return Task.FromResult(isLastConnection);
    }

    public Task<int[]> GetOnlineUsers()
    {
        var onlineUserIds = _onlineUsers.Keys.ToArray();
        return Task.FromResult(onlineUserIds);
    }

    public Task<bool> IsUserOnline(int userId)
    {
        return Task.FromResult(_onlineUsers.ContainsKey(userId));
    }
}
