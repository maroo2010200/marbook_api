using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Extensions;
using MarbookApi.Hubs;
using MarbookApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController(
    AppDbContext dbContext,
    IHubContext<ChatHub> hubContext,
    ILogger<ConversationsController> logger) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHubContext<ChatHub> _hubContext = hubContext;
    private readonly ILogger<ConversationsController> _logger = logger;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<ConversationSummaryDto>>> GetConversations()
    {
        var userId = CurrentUserId;

        var conversations = await _dbContext.Conversations
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.SenderUser)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var result = new List<ConversationSummaryDto>(conversations.Count);

        foreach (var conv in conversations)
        {
            result.Add(await BuildSummaryDtoAsync(conv, userId));
        }

        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ChatUnreadCountDto>> GetUnreadCount()
    {
        var userId = CurrentUserId;

        var userParticipants = await _dbContext.ConversationParticipants
            .Where(p => p.UserId == userId)
            .ToListAsync();

        int totalUnreadMessages = 0;
        int unreadConversations = 0;

        foreach (var p in userParticipants)
        {
            var count = await GetUnreadCountAsync(p.ConversationId, userId, p.LastReadAt);

            if (count > 0)
            {
                totalUnreadMessages += count;
                unreadConversations++;
            }
        }

        return Ok(new ChatUnreadCountDto
        {
            TotalUnreadMessages = totalUnreadMessages,
            UnreadConversationsCount = unreadConversations
        });
    }

    [HttpPost("start")]
    public async Task<ActionResult<ConversationSummaryDto>> StartOrGetConversation([FromBody] StartConversationDto dto)
    {
        var currentUserId = CurrentUserId;

        if (dto.TargetUserId == currentUserId)
        {
            return BadRequest("You cannot start a conversation with yourself.");
        }

        var targetUser = await _dbContext.Users.FindAsync(dto.TargetUserId);
        if (targetUser == null)
        {
            return NotFound("Target user not found.");
        }

        var existingConversation = await _dbContext.Conversations
            .Where(c => c.Participants.Any(p => p.UserId == currentUserId) &&
                        c.Participants.Any(p => p.UserId == dto.TargetUserId) &&
                        c.Participants.Count == 2)
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.SenderUser)
            .FirstOrDefaultAsync();

        if (existingConversation != null)
        {
            if (!string.IsNullOrWhiteSpace(dto.InitialMessage))
            {
                await CreateAndDispatchMessageAsync(existingConversation, currentUserId, dto.InitialMessage.Trim());
            }

            return Ok(await BuildSummaryDtoAsync(existingConversation, currentUserId));
        }

        var now = DateTime.UtcNow;
        var newConversation = new Conversation
        {
            CreatedAt = now,
            UpdatedAt = now,
            Participants = new List<ConversationParticipant>
            {
                new() { UserId = currentUserId, JoinedAt = now, LastReadAt = now },
                new() { UserId = dto.TargetUserId, JoinedAt = now }
            }
        };

        _dbContext.Conversations.Add(newConversation);
        await _dbContext.SaveChangesAsync();

        _logger.LogUserAction("ConversationStarted", currentUserId, User.GetUsernameOrDefault(), "Conversation", newConversation.Id, $"TargetUserId={dto.TargetUserId}");

        if (!string.IsNullOrWhiteSpace(dto.InitialMessage))
        {
            await CreateAndDispatchMessageAsync(newConversation, currentUserId, dto.InitialMessage.Trim());
        }

        var created = await _dbContext.Conversations
            .Where(c => c.Id == newConversation.Id)
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.SenderUser)
            .FirstAsync();

        return Ok(await BuildSummaryDtoAsync(created, currentUserId));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConversationSummaryDto>> GetConversation(int id)
    {
        var currentUserId = CurrentUserId;

        var conversation = await _dbContext.Conversations
            .Where(c => c.Id == id && c.Participants.Any(p => p.UserId == currentUserId))
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
                .ThenInclude(m => m.SenderUser)
            .FirstOrDefaultAsync();

        if (conversation == null)
        {
            return NotFound("Conversation not found.");
        }

        return Ok(await BuildSummaryDtoAsync(conversation, currentUserId));
    }

    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<PagedResult<MessageDto>>> GetMessages(
        int id,
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? search)
    {
        var currentUserId = CurrentUserId;

        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == id && cp.UserId == currentUserId);

        if (!isParticipant)
        {
            return Forbid();
        }

        var query = _dbContext.Messages
            .Where(m => m.ConversationId == id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => !m.IsDeleted && EF.Functions.ILike(m.Content, $"%{term}%"));
        }

        var totalCount = await query.CountAsync();
        var skip = (pagination.PageNumber - 1) * pagination.PageSize;

        var rawMessages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip)
            .Take(pagination.PageSize)
            .Include(m => m.SenderUser)
            .ToListAsync();

        if (string.IsNullOrWhiteSpace(search))
        {
            rawMessages.Reverse();
        }

        var items = rawMessages.Select(m => MapToDto(m, currentUserId)).ToList();

        return Ok(new PagedResult<MessageDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        });
    }

    [HttpGet("{id:int}/media")]
    public async Task<ActionResult<List<MessageDto>>> GetSharedMedia(int id)
    {
        var currentUserId = CurrentUserId;

        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == id && cp.UserId == currentUserId);

        if (!isParticipant)
        {
            return Forbid();
        }

        var rawMessages = await _dbContext.Messages
            .Where(m => m.ConversationId == id && !m.IsDeleted &&
                (EF.Functions.ILike(m.Content, "%http://%") || EF.Functions.ILike(m.Content, "%https://%")))
            .OrderByDescending(m => m.CreatedAt)
            .Take(300)
            .Include(m => m.SenderUser)
            .ToListAsync();

        var items = rawMessages.Select(m => MapToDto(m, currentUserId)).ToList();

        return Ok(items);
    }

    [HttpPost("{id:int}/messages")]
    [EnableRateLimiting("chat-policy")]
    public async Task<ActionResult<MessageDto>> SendMessage(int id, [FromBody] SendMessageDto dto)
    {
        var currentUserId = CurrentUserId;

        var conversation = await _dbContext.Conversations
            .Where(c => c.Id == id && c.Participants.Any(p => p.UserId == currentUserId))
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync();

        if (conversation == null)
        {
            return NotFound("Conversation not found or you are not a participant.");
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest("Message content cannot be empty.");
        }

        var messageDto = await CreateAndDispatchMessageAsync(conversation, currentUserId, dto.Content.Trim());

        return CreatedAtAction(nameof(GetMessages), new { id = conversation.Id }, messageDto);
    }

    [HttpPut("{id:int}/messages/{messageId:int}")]
    public async Task<ActionResult<MessageDto>> EditMessage(
        int id,
        int messageId,
        [FromBody] UpdateMessageDto dto)
    {
        var currentUserId = CurrentUserId;

        var message = await _dbContext.Messages
            .Include(m => m.SenderUser)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id);

        if (message == null)
        {
            return NotFound("Message not found.");
        }

        if (message.SenderUserId != currentUserId)
        {
            return Forbid();
        }

        if (message.IsDeleted)
        {
            return BadRequest("Cannot edit a deleted message.");
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest("Message content cannot be empty.");
        }

        var now = DateTime.UtcNow;
        message.Content = dto.Content.Trim();
        message.UpdatedAt = now;

        await _dbContext.SaveChangesAsync();

        var messageDto = MapToDto(message, currentUserId);
        await BroadcastToConversationAsync(id, "MessageEdited", messageDto);

        _logger.LogUserAction("MessageEdited", currentUserId, message.SenderUser.Username, "Message", messageId, $"ConversationId={id}");

        return Ok(messageDto);
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var currentUserId = CurrentUserId;

        var participant = await _dbContext.ConversationParticipants
            .FirstOrDefaultAsync(cp => cp.ConversationId == id && cp.UserId == currentUserId);

        if (participant == null)
        {
            return NotFound("Conversation not found or you are not a participant.");
        }

        participant.LastReadAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var readPayload = new
        {
            ConversationId = id,
            ReaderUserId = currentUserId,
            ReadAt = participant.LastReadAt
        };
        await BroadcastToConversationAsync(id, "MessagesRead", readPayload);

        return NoContent();
    }

    [HttpDelete("{id:int}/messages/{messageId:int}")]
    public async Task<IActionResult> DeleteMessage(int id, int messageId)
    {
        var currentUserId = CurrentUserId;

        var message = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id);

        if (message == null)
        {
            return NotFound("Message not found.");
        }

        if (message.SenderUserId != currentUserId)
        {
            return Forbid();
        }

        if (message.IsDeleted)
        {
            return NoContent();
        }

        var now = DateTime.UtcNow;
        message.IsDeleted = true;
        message.Content = "This message was deleted";
        message.UpdatedAt = now;

        await _dbContext.SaveChangesAsync();

        var deletePayload = new
        {
            ConversationId = id,
            MessageId = messageId,
            Content = "This message was deleted",
            IsDeleted = true,
            UpdatedAt = now
        };
        await BroadcastToConversationAsync(id, "MessageDeleted", deletePayload);

        _logger.LogUserAction("MessageDeleted", currentUserId, User.GetUsernameOrDefault(), "Message", messageId, $"ConversationId={id}");

        return NoContent();
    }

    [HttpPost("{id:int}/messages/{messageId:int}/pin")]
    public async Task<IActionResult> TogglePinMessage(int id, int messageId)
    {
        var currentUserId = CurrentUserId;

        var message = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id);

        if (message == null)
        {
            return NotFound("Message not found.");
        }

        // Any conversation participant can pin or unpin messages in this conversation
        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == id && cp.UserId == currentUserId);

        if (!isParticipant)
        {
            return Forbid();
        }

        message.IsPinned = !message.IsPinned;

        if (message.IsPinned)
        {
            // Unpin any other pinned message in this conversation
            var otherPinned = await _dbContext.Messages
                .Where(m => m.ConversationId == id && m.Id != messageId && m.IsPinned)
                .ToListAsync();
            foreach (var op in otherPinned)
            {
                op.IsPinned = false;
            }
        }

        await _dbContext.SaveChangesAsync();

        await BroadcastToConversationAsync(id, "MessagePinned", new
        {
            conversationId = id,
            messageId,
            isPinned = message.IsPinned
        });

        _logger.LogUserAction(message.IsPinned ? "MessagePinned" : "MessageUnpinned", currentUserId, User.GetUsernameOrDefault(), "Message", messageId, $"ConversationId={id}");

        return Ok(new { conversationId = id, messageId, isPinned = message.IsPinned });
    }

    private async Task<MessageDto> CreateAndDispatchMessageAsync(Conversation conversation, int senderUserId, string content)
    {
        var now = DateTime.UtcNow;

        var message = new Message
        {
            ConversationId = conversation.Id,
            SenderUserId = senderUserId,
            Content = content,
            CreatedAt = now,
            UpdatedAt = null,
            IsDeleted = false
        };

        _dbContext.Messages.Add(message);
        conversation.UpdatedAt = now;

        var senderParticipant = conversation.Participants.FirstOrDefault(p => p.UserId == senderUserId);
        if (senderParticipant != null)
        {
            senderParticipant.LastReadAt = now;
        }

        await _dbContext.SaveChangesAsync();

        var sender = await _dbContext.Users.FindAsync(senderUserId);
        _logger.LogUserAction("MessageSent", senderUserId, sender?.Username, "Message", message.Id, $"ConversationId={conversation.Id}");

        var senderMessageDto = MapToDto(message, currentUserId: senderUserId, sender);
        var recipientMessageDto = MapToDto(message, currentUserId: -1, sender);

        var groupName = ChatHub.GetConversationGroupName(conversation.Id);

        // 1. Send to all in conversation group with recipient view
        await _hubContext.Clients.Group(groupName).SendAsync("ReceiveMessage", recipientMessageDto);

        // 2. Send specifically to sender with IsMine = true
        await _hubContext.Clients.User(senderUserId.ToString()).SendAsync("ReceiveMessage", senderMessageDto);

        // 3. Send update notification to other participant(s) for sidebar/badges with IsMine = false
        var otherParticipants = conversation.Participants.Where(p => p.UserId != senderUserId).ToList();
        foreach (var other in otherParticipants)
        {
            await _hubContext.Clients.User(other.UserId.ToString()).SendAsync("ReceiveMessage", recipientMessageDto);

            var unreadCount = await GetUnreadCountAsync(conversation.Id, other.UserId, other.LastReadAt);

            var summary = new ConversationSummaryDto
            {
                Id = conversation.Id,
                UpdatedAt = conversation.UpdatedAt,
                OtherParticipant = MapToUserSearchDto(sender),
                LastMessage = recipientMessageDto,
                UnreadCount = unreadCount,
                OtherParticipantLastReadAt = senderParticipant?.LastReadAt
            };

            await _hubContext.Clients.User(other.UserId.ToString()).SendAsync("ConversationUpdated", summary);
        }

        return senderMessageDto;
    }

    private async Task<ConversationSummaryDto> BuildSummaryDtoAsync(Conversation conversation, int currentUserId)
    {
        var myParticipant = conversation.Participants.FirstOrDefault(p => p.UserId == currentUserId);
        var otherParticipant = conversation.Participants.FirstOrDefault(p => p.UserId != currentUserId)
                               ?? myParticipant;

        var lastMsg = conversation.Messages.FirstOrDefault();
        var lastMsgDto = lastMsg != null ? MapToDto(lastMsg, currentUserId) : null;
        var unreadCount = await GetUnreadCountAsync(conversation.Id, currentUserId, myParticipant?.LastReadAt);

        return new ConversationSummaryDto
        {
            Id = conversation.Id,
            UpdatedAt = conversation.UpdatedAt,
            OtherParticipant = MapToUserSearchDto(otherParticipant?.User),
            LastMessage = lastMsgDto,
            UnreadCount = unreadCount,
            OtherParticipantLastReadAt = otherParticipant?.LastReadAt
        };
    }
    private static MessageDto MapToDto(Message message, int currentUserId, User? senderUser = null)
    {
        var sender = senderUser ?? message.SenderUser;
        return new MessageDto
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderUserId = message.SenderUserId,
            SenderName = sender?.Name ?? string.Empty,
            SenderAvatarUrl = sender?.AvatarUrl,
            Content = message.IsDeleted ? "This message was deleted" : message.Content,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            IsDeleted = message.IsDeleted,
            IsMine = message.SenderUserId == currentUserId,
            IsPinned = message.IsPinned
        };
    }

    private static UserSearchDto MapToUserSearchDto(User? user) => new()
    {
        Id = user?.Id ?? 0,
        Name = user?.Name ?? "Unknown",
        Username = user?.Username,
        AvatarUrl = user?.AvatarUrl
    };

    private Task<int> GetUnreadCountAsync(int conversationId, int userId, DateTime? lastReadAt)
    {
        return _dbContext.Messages.CountAsync(m =>
            m.ConversationId == conversationId &&
            m.SenderUserId != userId &&
            !m.IsDeleted &&
            (lastReadAt == null || m.CreatedAt > lastReadAt));
    }

    private async Task BroadcastToConversationAsync(int conversationId, string method, object payload)
    {
        var groupName = ChatHub.GetConversationGroupName(conversationId);
        await _hubContext.Clients.Group(groupName).SendAsync(method, payload);

        var participantUserIds = await _dbContext.ConversationParticipants
            .Where(cp => cp.ConversationId == conversationId)
            .Select(cp => cp.UserId.ToString())
            .ToListAsync();
        await _hubContext.Clients.Users(participantUserIds).SendAsync(method, payload);
    }
}
