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
                    MessageContent = "Has left channel",
                    User = username,
                    Date = DateTime.Now.ToString("HH:mm dd/MM/yyyy"),
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

            RedisHelper.SubscribeToChannel(_redis, channelName, _hub);
            HttpContext.Session.SetString(_currentChannelSessionKey, channelName);

            var message = new
            {
                MessageContent = "Has joined channel",
                User = username,
                Date = DateTime.Now.ToString("HH:mm dd/MM/yyyy"),
            };

            string jsonMessage = JsonConvert.SerializeObject(message);

            await _redis.GetSubscriber().PublishAsync(channelName, jsonMessage);

            return View(model: channelName);
        }

        public async Task<IActionResult> Message(string message, string channelName)
        {
            var username = HttpContext.Session.GetString(_usernameSessionKey);
            if(string.IsNullOrWhiteSpace(username))
                return Unauthorized();

            if(string.IsNullOrWhiteSpace(channelName))
                return BadRequest();

            if(string.IsNullOrWhiteSpace(message))
                return BadRequest();

            channelName = channelName.Trim().ToLower();
            var groupChats = await _redis.GetDatabase().ListRangeAsync(_channelsRedisKeys);
            if (!groupChats.Contains(channelName))
                return BadRequest();

            var userMessage = new
            {
                MessageContent = message,
                User = username,
                Date = DateTime.Now.ToString("HH:mm dd/MM/yyyy"),
            };

            var userMessageJson = JsonConvert.SerializeObject(userMessage);

            _redis.GetSubscriber().Publish(channelName, userMessageJson);

            return Ok();
        } 
    }
}
