using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AnonymousFzBotTest")]

namespace AnonymousFzBot
{
    public class State
    {
        private readonly SerializedState _innerState;

        internal class SerializedState
        {
            static SerializedState()
            {
                var stateFile = Environment.GetEnvironmentVariable("statefile");
                if (!string.IsNullOrWhiteSpace(stateFile))
                {
                    StateFile = stateFile;
                }
            }

            public static string StateFile = "state.json";

            public Dictionary<long, Dictionary<int, int>> ForwardedMessageIds { get; set; } = new Dictionary<long, Dictionary<int, int>>(); // ChatId of user -> OriginalMessageId -> ForwardedMessageId  

            [JsonIgnore]
            public Dictionary<int, List<int>> UserMessages { get; set; } = new Dictionary<int, List<int>>(); // user messages
        
            public Dictionary<int, long> EnabledUsers { get; set; } = new Dictionary<int, long>(); // User -> ChatId
            public List<int> BannedUsers { get; set; } = new List<int>();

            public Dictionary<string, DateTime> LastOnline { get; } = new Dictionary<string, DateTime>();

            public static SerializedState Load()
            {
                if (File.Exists(StateFile))
                {
                    return JsonSerializer.Deserialize<SerializedState>(File.ReadAllText(StateFile));
                }
                return new SerializedState();
            }

            public void Save()
            {
                File.WriteAllText(StateFile, JsonSerializer.Serialize(this));
            }
        }

        internal State(SerializedState innerState)
        {
            _innerState = innerState;
        }
        

        public static State Load() => new State(SerializedState.Load());

        public void Save()
        {
            _innerState.Save();
        }
        
        public (int originalMessageId, bool sentByMe) GetProxiedMessageOriginalId(int receivedByUserId, int proxiedMessageId)
        {
            // 0 - no replyTo / noId
            if (proxiedMessageId == 0)
                return (0, false);
            
            if (!_innerState.UserMessages.TryGetValue(receivedByUserId, out var myMessages))
            {
                _innerState.UserMessages[receivedByUserId] = myMessages = new List<int>();
            }

            if (myMessages.Contains(proxiedMessageId))
            {
                return (proxiedMessageId, true); // was sent by me;
            }

            if (!_innerState.ForwardedMessageIds.TryGetValue(receivedByUserId, out var proxiedForMe))
            {
                _innerState.ForwardedMessageIds[receivedByUserId] = proxiedForMe = new Dictionary<int, int>();
            }

            return (proxiedForMe.FirstOrDefault(v => v.Value == proxiedMessageId).Key, false); // or 0, which means - do not proxy;
        }

        public (int proxiedId, bool sendToMe) GetProxyOfMessageForUser(int targetUser, int originalMessageId)
        {
            if (originalMessageId == 0)
                return (0, false);
            
            if (!_innerState.UserMessages.TryGetValue(targetUser, out var myMessages))
            {
                _innerState.UserMessages[targetUser] = myMessages = new List<int>();
            }

            if (myMessages.Contains(originalMessageId))
            {
                return (originalMessageId, true); // was sent by me;
            }

            if (!_innerState.ForwardedMessageIds.TryGetValue(targetUser, out var proxiedForMe))
            {
                _innerState.ForwardedMessageIds[targetUser] = proxiedForMe = new Dictionary<int, int>();
            }

            if (proxiedForMe.TryGetValue(originalMessageId, out var proxiedId))
                return (proxiedId, false);

            return (0, false);
        }

        public List<(int user, long chat)> GetUsers() => _innerState.EnabledUsers.Select(v=>(v.Key,v.Value)).ToList();

        public bool IsBanned(in int fromId) => _innerState.BannedUsers.Contains(fromId);

        public bool IsEnabled(in int fromId) => _innerState.EnabledUsers.ContainsKey(fromId);

        public void Enable(in int fromId, in long chatId) => _innerState.EnabledUsers.Add(fromId, chatId);

        public void Ban(in int user) => _innerState.BannedUsers.Add(user);

        public void RecordUserSentMessage(in int fromId, in int messageId)
        {
            if (!_innerState.UserMessages.TryGetValue(fromId, out var userMessages))
            {
                userMessages = new List<int>();
                _innerState.UserMessages[fromId] = userMessages;
            }

            userMessages.Add(messageId);

        }

        public int GetUserIdByMessageId(int originalMessageId) => _innerState.UserMessages.FirstOrDefault(v => v.Value.Contains(originalMessageId)).Key;

        public void RecordMessageWasForwarded(int targetUser, int originalId, int newId)
        {
            if (!_innerState.ForwardedMessageIds.TryGetValue(targetUser, out var forwardeds))
            {
                forwardeds = new Dictionary<int, int>();
                _innerState.ForwardedMessageIds[targetUser] = forwardeds;
            }

            forwardeds[originalId] = newId;

        }

        public void Disable(int userToRemove)
        {
            _innerState.EnabledUsers.Remove(userToRemove);
            _innerState.ForwardedMessageIds.Remove(userToRemove);
            _innerState.UserMessages.Remove(userToRemove);
        }

        public void StoreLastOnline(string fromUsername)
        {
            _innerState.LastOnline[fromUsername] = DateTime.UtcNow;
        }

        public IEnumerable<string> GetLastOnline() => _innerState.LastOnline.OrderByDescending(v => v.Value).Select(v => v.Key);
    }
}