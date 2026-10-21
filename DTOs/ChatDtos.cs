using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class MessageDto
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatarUrl { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsMine { get; set; }
}

public class UpdateMessageDto
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}

public class ConversationSummaryDto
{
    public int Id { get; set; }
    public DateTime UpdatedAt { get; set; }
    public UserSearchDto OtherParticipant { get; set; } = null!;
    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }
}

public class SendMessageDto
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}

public class StartConversationDto
{
    [Required]
    public int TargetUserId { get; set; }

    [StringLength(2000)]
    public string? InitialMessage { get; set; }
}

public class ChatUnreadCountDto
{
    public int TotalUnreadMessages { get; set; }
    public int UnreadConversationsCount { get; set; }
}

public class TypingNotificationDto
{
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsTyping { get; set; }
}
