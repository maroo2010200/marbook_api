using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MarbookApi.Hubs;

[Authorize]
public class NotificationHub : Hub
{
}
