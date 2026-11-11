using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Hubs;

[Authorize]
public class ChatHub(
    AppDbContext dbContext,
    IPresenceTracker presenceTracker,
    ILogger<ChatHub> logger) : Hub
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPresenceTracker _presenceTracker = presenceTracker;
    private readonly ILogger<ChatHub> _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var userIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdStr, out var userId))
        {
            var isOnline = await _presenceTracker.UserConnected(userId, Context.ConnectionId);
            if (isOnline)
            {
                await Clients.Others.SendAsync("UserPresenceChanged", new { userId, isOnline = true });
            }

            var currentOnlineUserIds = await _presenceTracker.GetOnlineUsers();
            await Clients.Caller.SendAsync("GetOnlineUsers", currentOnlineUserIds);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdStr, out var userId))
        {
            var isOffline = await _presenceTracker.UserDisconnected(userId, Context.ConnectionId);
            if (isOffline)
            {
                await Clients.Others.SendAsync("UserPresenceChanged", new { userId, isOnline = false });
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(int conversationId)
    {
        var userIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdStr, out var userId))
        {
            var isParticipant = await _dbContext.ConversationParticipants
                .AnyAsync(cp => cp.ConversationId == conversationId && cp.UserId == userId);

            if (isParticipant)
            {
                var groupName = GetConversationGroupName(conversationId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                _logger.LogInformation("User {UserId} joined SignalR group {GroupName}", userId, groupName);
            }
        }
    }

    public async Task LeaveConversation(int conversationId)
    {
        var groupName = GetConversationGroupName(conversationId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task SendTyping(int conversationId, bool isTyping)
    {
        var userIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdStr, out var userId))
        {
            var isParticipant = await _dbContext.ConversationParticipants
                .AnyAsync(cp => cp.ConversationId == conversationId && cp.UserId == userId);

            if (isParticipant)
            {
                var user = await _dbContext.Users.FindAsync(userId);
                var userName = user?.Name ?? "Someone";

                var dto = new TypingNotificationDto
                {
                    ConversationId = conversationId,
                    UserId = userId,
                    UserName = userName,
                    IsTyping = isTyping
                };

                // 1. Broadcast to conversation group
                var groupName = GetConversationGroupName(conversationId);
                await Clients.OthersInGroup(groupName).SendAsync("UserTyping", dto);

                // 2. Also broadcast directly to other participant(s) via User channels
                var otherParticipantUserIds = await _dbContext.ConversationParticipants
                    .Where(cp => cp.ConversationId == conversationId && cp.UserId != userId)
                    .Select(cp => cp.UserId.ToString())
                    .ToListAsync();

                await Clients.Users(otherParticipantUserIds).SendAsync("UserTyping", dto);
            }
        }
    }

    public static string GetConversationGroupName(int conversationId) => $"conversation_{conversationId}";
}
