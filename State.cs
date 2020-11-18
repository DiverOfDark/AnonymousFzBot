using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AnonymousFzBot
{
    internal class State : IDisposable
    {
        public void Dispose()
        {
        }
        
        public Dictionary<long, Dictionary<int, int>> ForwardedMessageIds { get; set; } = new Dictionary<long, Dictionary<int, int>>(); // ChatId of user -> OriginalMessageId -> ForwardedMessageId  

        public Dictionary<int, List<int>> UserMessages { get; set; } = new Dictionary<int, List<int>>(); // user messages
        
        public Dictionary<int, long> EnabledUsers { get; set; } = new Dictionary<int, long>(); // User -> ChatId
        public List<int> BannedUsers { get; set; } = new List<int>();

        public static State Load()
        {
            if (File.Exists("state.json"))
            {
                return JsonSerializer.Deserialize<State>(File.ReadAllText("state.json"));
            }
            return new State();
        }

        public void Save()
        {
            File.WriteAllText("state.json", JsonSerializer.Serialize(this));
        }
    }
}