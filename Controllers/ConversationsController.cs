using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Hubs;
using MarbookApi.Models;
using Microsoft.AspNetCore.Authorization;
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

        var result = new List<ConversationSummaryDto>();

        foreach (var conv in conversations)
        {
            var myParticipant = conv.Participants.FirstOrDefault(p => p.UserId == userId);
            var otherParticipant = conv.Participants.FirstOrDefault(p => p.UserId != userId)
                                   ?? myParticipant;

            var lastMsg = conv.Messages.FirstOrDefault();
            MessageDto? lastMsgDto = null;
            if (lastMsg != null)
            {
                lastMsgDto = new MessageDto
                {
                    Id = lastMsg.Id,
                    ConversationId = conv.Id,
                    SenderUserId = lastMsg.SenderUserId,
                    SenderName = lastMsg.SenderUser?.Name ?? string.Empty,
                    SenderAvatarUrl = lastMsg.SenderUser?.AvatarUrl,
                    Content = lastMsg.IsDeleted ? "This message was deleted" : lastMsg.Content,
                    CreatedAt = lastMsg.CreatedAt,
                    UpdatedAt = lastMsg.UpdatedAt,
                    IsDeleted = lastMsg.IsDeleted,
                    IsMine = lastMsg.SenderUserId == userId
                };
            }

            var lastRead = myParticipant?.LastReadAt;
            var unreadCount = await _dbContext.Messages
                .CountAsync(m => m.ConversationId == conv.Id &&
                                 m.SenderUserId != userId &&
                                 !m.IsDeleted &&
                                 (lastRead == null || m.CreatedAt > lastRead));

            result.Add(new ConversationSummaryDto
            {
                Id = conv.Id,
                UpdatedAt = conv.UpdatedAt,
                OtherParticipant = new UserSearchDto
                {
                    Id = otherParticipant?.User.Id ?? 0,
                    Name = otherParticipant?.User.Name ?? "Unknown",
                    Username = otherParticipant?.User.Username,
                    AvatarUrl = otherParticipant?.User.AvatarUrl
                },
                LastMessage = lastMsgDto,
                UnreadCount = unreadCount
            });
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
            var count = await _dbContext.Messages
                .CountAsync(m => m.ConversationId == p.ConversationId &&
                                 m.SenderUserId != userId &&
                                 !m.IsDeleted &&
                                 (p.LastReadAt == null || m.CreatedAt > p.LastReadAt));

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
        [FromQuery] PaginationParams pagination)
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

        var totalCount = await query.CountAsync();
        var skip = (pagination.PageNumber - 1) * pagination.PageSize;

        var rawMessages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip)
            .Take(pagination.PageSize)
            .Include(m => m.SenderUser)
            .ToListAsync();

        rawMessages.Reverse();

        var items = rawMessages.Select(m => new MessageDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            SenderUserId = m.SenderUserId,
            SenderName = m.SenderUser?.Name ?? string.Empty,
            SenderAvatarUrl = m.SenderUser?.AvatarUrl,
            Content = m.IsDeleted ? "This message was deleted" : m.Content,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            IsDeleted = m.IsDeleted,
            IsMine = m.SenderUserId == currentUserId
        }).ToList();

        return Ok(new PagedResult<MessageDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        });
    }

    [HttpPost("{id:int}/messages")]
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

        var messageDto = new MessageDto
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderUserId = message.SenderUserId,
            SenderName = message.SenderUser?.Name ?? string.Empty,
            SenderAvatarUrl = message.SenderUser?.AvatarUrl,
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            IsDeleted = false,
            IsMine = true
        };

        var groupName = ChatHub.GetConversationGroupName(id);
        await _hubContext.Clients.Group(groupName).SendAsync("MessageEdited", messageDto);

        var participantUserIds = await _dbContext.ConversationParticipants
            .Where(cp => cp.ConversationId == id)
            .Select(cp => cp.UserId.ToString())
            .ToListAsync();
        await _hubContext.Clients.Users(participantUserIds).SendAsync("MessageEdited", messageDto);

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

        var groupName = ChatHub.GetConversationGroupName(id);
        var readPayload = new
        {
            ConversationId = id,
            ReaderUserId = currentUserId,
            ReadAt = participant.LastReadAt
        };
        await _hubContext.Clients.Group(groupName).SendAsync("MessagesRead", readPayload);

        var participantUserIds = await _dbContext.ConversationParticipants
            .Where(cp => cp.ConversationId == id)
            .Select(cp => cp.UserId.ToString())
            .ToListAsync();
        await _hubContext.Clients.Users(participantUserIds).SendAsync("MessagesRead", readPayload);

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

        var groupName = ChatHub.GetConversationGroupName(id);
        var deletePayload = new
        {
            ConversationId = id,
            MessageId = messageId,
            Content = "This message was deleted",
            IsDeleted = true,
            UpdatedAt = now
        };
        await _hubContext.Clients.Group(groupName).SendAsync("MessageDeleted", deletePayload);

        var participantUserIds = await _dbContext.ConversationParticipants
            .Where(cp => cp.ConversationId == id)
            .Select(cp => cp.UserId.ToString())
            .ToListAsync();
        await _hubContext.Clients.Users(participantUserIds).SendAsync("MessageDeleted", deletePayload);

        return NoContent();
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

        var senderMessageDto = new MessageDto
        {
            Id = message.Id,
            ConversationId = conversation.Id,
            SenderUserId = senderUserId,
            SenderName = sender?.Name ?? string.Empty,
            SenderAvatarUrl = sender?.AvatarUrl,
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            UpdatedAt = null,
            IsDeleted = false,
            IsMine = true
        };

        var recipientMessageDto = new MessageDto
        {
            Id = message.Id,
            ConversationId = conversation.Id,
            SenderUserId = senderUserId,
            SenderName = sender?.Name ?? string.Empty,
            SenderAvatarUrl = sender?.AvatarUrl,
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            UpdatedAt = null,
            IsDeleted = false,
            IsMine = false
        };

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

            var unreadCount = await _dbContext.Messages
                .CountAsync(m => m.ConversationId == conversation.Id &&
                                 m.SenderUserId != other.UserId &&
                                 !m.IsDeleted &&
                                 (other.LastReadAt == null || m.CreatedAt > other.LastReadAt));

            var summary = new ConversationSummaryDto
            {
                Id = conversation.Id,
                UpdatedAt = conversation.UpdatedAt,
                OtherParticipant = new UserSearchDto
                {
                    Id = sender?.Id ?? senderUserId,
                    Name = sender?.Name ?? string.Empty,
                    Username = sender?.Username,
                    AvatarUrl = sender?.AvatarUrl
                },
                LastMessage = recipientMessageDto,
                UnreadCount = unreadCount
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
        MessageDto? lastMsgDto = null;
        if (lastMsg != null)
        {
            lastMsgDto = new MessageDto
            {
                Id = lastMsg.Id,
                ConversationId = conversation.Id,
                SenderUserId = lastMsg.SenderUserId,
                SenderName = lastMsg.SenderUser?.Name ?? string.Empty,
                SenderAvatarUrl = lastMsg.SenderUser?.AvatarUrl,
                Content = lastMsg.IsDeleted ? "This message was deleted" : lastMsg.Content,
                CreatedAt = lastMsg.CreatedAt,
                UpdatedAt = lastMsg.UpdatedAt,
                IsDeleted = lastMsg.IsDeleted,
                IsMine = lastMsg.SenderUserId == currentUserId
            };
        }

        var lastRead = myParticipant?.LastReadAt;
        var unreadCount = await _dbContext.Messages
            .CountAsync(m => m.ConversationId == conversation.Id &&
                             m.SenderUserId != currentUserId &&
                             !m.IsDeleted &&
                             (lastRead == null || m.CreatedAt > lastRead));

        return new ConversationSummaryDto
        {
            Id = conversation.Id,
            UpdatedAt = conversation.UpdatedAt,
            OtherParticipant = new UserSearchDto
            {
                Id = otherParticipant?.User.Id ?? 0,
                Name = otherParticipant?.User.Name ?? "Unknown",
                Username = otherParticipant?.User.Username,
                AvatarUrl = otherParticipant?.User.AvatarUrl
            },
            LastMessage = lastMsgDto,
            UnreadCount = unreadCount
        };
    }
}
