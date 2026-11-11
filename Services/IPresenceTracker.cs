namespace MarbookApi.Services;

public interface IPresenceTracker
{
    Task<bool> UserConnected(int userId, string connectionId);
    Task<bool> UserDisconnected(int userId, string connectionId);
    Task<int[]> GetOnlineUsers();
    Task<bool> IsUserOnline(int userId);
}

