﻿using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace redis.Helper
{
    public static class RedisHelper
    {
        private static bool _firstTimeRunningFlushDatabase = true;
        private static readonly object _lockFlushDatabase = new();

        private static readonly object _lockAllChatsSubscribe = new();

        private static bool _firstTimeRunningSubscribeToCreatedChannels = true;
        private static readonly object _lockCreatedChannels = new();
        private static readonly Dictionary<string, List<string>> _channelsWithUsers = new();


        public static readonly string ChannelsKey = "Channels";

        public static void FlushRedisDatabase(IConnectionMultiplexer _redis)
        {
            lock (_lockFlushDatabase)
            {
                if (!_firstTimeRunningFlushDatabase)
                    return;

                _redis.GetServer("localhost:6379").FlushAllDatabasesAsync();

                _firstTimeRunningFlushDatabase = false;
            }
        }

        public static void SubscribeToChannel(IConnectionMultiplexer _redis, string channelName, string username, IHubContext<ChannelHub> hub)
        {
            lock (_lockAllChatsSubscribe)
            {
                if (_channelsWithUsers.TryGetValue(channelName, out var users))
                {
                    if (users.Contains(username))
                        return;
                    users.Add(username.Trim().ToLower());
                } 
                else
                {
                    _channelsWithUsers.Add(channelName, new List<string>{ username });
                }

                _redis.GetSubscriber().Subscribe(channelName, async (redisChanel, message) =>
                {
                    await hub.Clients.All.SendAsync(redisChanel, (string)message);
                }); 
            }
        }

        public static void SubscribeToCreatedChannels(IConnectionMultiplexer _redis, IHubContext<ChannelHub> hub)
        {
            lock (_lockCreatedChannels)
            {
                if (!_firstTimeRunningSubscribeToCreatedChannels)
                    return;

                _firstTimeRunningSubscribeToCreatedChannels = false;

                _redis.GetSubscriber().Subscribe(ChannelsKey, async (_, message) =>
                {
                    await hub.Clients.All.SendAsync(ChannelsKey, (string)message);
                });
            }
        }
    }
}
