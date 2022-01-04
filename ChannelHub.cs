using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace redis
{
    public class ChannelHub : Hub
    {
        public Task SendMessage(string room, string user, string message)
        {
            return Clients.Group(room).SendAsync("Send", user, message);
        }

        public async Task AddToGroup(string user, string groupName)
        {
            await Groups.AddToGroupAsync(user, groupName);

            await Clients.Group(groupName).SendAsync("Send", $"{user} has joined the group {groupName}.");
        }

        public async Task RemoveFromGroup(string user, string groupName)
        {
            await Groups.RemoveFromGroupAsync(user, groupName);

            await Clients.Group(groupName).SendAsync("Send", $"{user} has left the group {groupName}.");
        }

    }
}
