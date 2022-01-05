using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using Newtonsoft.Json;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using redis.Helper;

namespace redis.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _usernameSessionKey = "username";
        private readonly string _currentChannelSessionKey = "currentChannel";
        private readonly string _usernameRedisKeys = "redis.usernames";
        private readonly string _channelsRedisKeys = "redis.channels";

        private readonly IConnectionMultiplexer _redis;
        private readonly IHubContext<ChannelHub> _hub;

        public HomeController(IHubContext<ChannelHub> hub, IConnectionMultiplexer redis)
        {
            _hub = hub;
            _redis = redis;
            RedisHelper.FlushRedisDatabase(_redis);
            RedisHelper.SubscribeToCreatedChannels(_redis, _hub);
        }

        public IActionResult Index()
        {
            string username = HttpContext.Session.GetString(_usernameSessionKey);
            if(string.IsNullOrWhiteSpace(username))
                return View();  
            return RedirectToAction("Channels");
        }

        [HttpPost]
        public async Task<IActionResult> Index(string username)
        { 
            username = username?.Trim().ToLower();
            if (string.IsNullOrEmpty(username)) 
                return View();

            var listOfSelectedUsernames = await _redis.GetDatabase().ListRangeAsync(_usernameRedisKeys);
            if (listOfSelectedUsernames.Contains(username))
                return View();

            await _redis.GetDatabase().ListRightPushAsync(_usernameRedisKeys, username);
            HttpContext.Session.SetString(_usernameSessionKey, username); 

            return RedirectToAction("Channels");
        }

        public async Task<IActionResult> Channels()
        {
            string username = HttpContext.Session.GetString(_usernameSessionKey);
            if(string.IsNullOrWhiteSpace(username))
                return RedirectToAction("Index"); 

            string previousChannel = HttpContext.Session.GetString(_currentChannelSessionKey);

            if (!string.IsNullOrEmpty(previousChannel))
            {
                var userHasLeft = new 
                {
                    Message = "This user has left this chat",
                    User = username,
                };
                
                string message = JsonConvert.SerializeObject(userHasLeft);

                await _redis.GetSubscriber().PublishAsync(previousChannel, message);
            }

            var t = await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys);
            IList<string> channels = (await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys))
                .Select(c => c.ToString())
                .ToList() ?? new List<string>();

            return View(channels);
        }

        [HttpPost]
        public async Task<IActionResult> AddChannel(string channelName)
        {
            string username = HttpContext.Session.GetString(_usernameSessionKey);
            if(string.IsNullOrWhiteSpace(username))
                return RedirectToAction("Index");
 
            channelName = channelName?.Trim().ToLower();
            var channels = await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys);
            if (string.IsNullOrEmpty(channelName) || channels.Contains(channelName))
                return RedirectToAction("Channels");

            await _redis.GetDatabase().ListRightPushAsync(_channelsRedisKeys, channelName);
            var t = await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys);
            await _redis.GetSubscriber().PublishAsync(RedisHelper.ChannelsKey, channelName);

            return RedirectToAction("Channels");
        }

        public async Task<IActionResult> Channel(string channelName)
        {
            string username = HttpContext.Session.GetString(_usernameSessionKey);
            if(string.IsNullOrWhiteSpace(username))
                return RedirectToAction("Index"); 

           channelName = channelName.Trim().ToLower();
            var channels = await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys);
            if (!channels.Contains(channelName))
                return RedirectToAction("Channels");

            RedisHelper.SubscribeToChannel(_redis, channelName, username, _hub);
            HttpContext.Session.SetString(_currentChannelSessionKey, channelName);

            var message = new
            {
                MessageContent = "This user has joined this chat",
                User = username
            };

            string jsonMessage = JsonConvert.SerializeObject(message);

            await _redis.GetSubscriber().PublishAsync(channelName, jsonMessage);

            return View(model: channelName);
        }
    }
}
